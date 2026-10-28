using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Domain;

/// <summary>
/// The application's single source of truth. Holds the complete dataset in memory (Sofie's data is
/// small) and writes every change through to durable storage in one transaction.
/// The in-memory state is only updated after the store confirmed the write, so a failed write
/// never leaves the app showing data that was not saved.
/// </summary>
public sealed class Ledger
{
    private readonly IDataStore _store;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private Ledger(IDataStore store, IClock clock, AppData data, BackupStatus backupStatus)
    {
        _store = store;
        _clock = clock;
        Data = data;
        BackupStatus = backupStatus;
    }

    public AppData Data { get; private set; }

    public BackupStatus BackupStatus { get; private set; }

    public IClock Clock => _clock;

    /// <summary>Raised after every successful change.</summary>
    public event Action? Changed;

    public static async Task<Ledger> LoadAsync(IDataStore store, IClock clock)
    {
        DataSerializer.LoadedStores loaded;
        try
        {
            loaded = DataSerializer.ParseAllStores(await store.LoadAllJsonAsync());
        }
        catch (UnsupportedDataVersionException)
        {
            throw;
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageException("De gemte data kunne ikke indlæses.", ex);
        }

        if (loaded.Data is null)
        {
            if (loaded.HasAnyRecords)
            {
                // Never overwrite something we do not understand.
                throw new StorageException("De gemte data er ufuldstændige (indstillingerne mangler). Gendan fra en backup.");
            }

            var fresh = DefaultData.Create(clock.UtcNow);
            await store.ReplaceAllAsync(DataSerializer.SerializeAllStores(fresh, loaded.BackupStatus), []);
            return new Ledger(store, clock, fresh, loaded.BackupStatus);
        }

        if (loaded.WasMigrated)
        {
            await store.ReplaceAllAsync(DataSerializer.SerializeAllStores(loaded.Data, loaded.BackupStatus), ReferencedAttachmentIds(loaded.Data));
        }

        var ledger = new Ledger(store, clock, loaded.Data, loaded.BackupStatus);
        await ledger.TryCleanUpOrphanAttachmentsAsync();
        return ledger;
    }

    public static HashSet<Guid> ReferencedAttachmentIds(AppData data) =>
        data.Expenses.Where(e => e.Attachment is not null).Select(e => e.Attachment!.Id).ToHashSet();

    // ---------------------------------------------------------------- Sessions and payments

    public async Task<SaveResult<Session>> SaveSessionAsync(SessionDraft draft)
    {
        draft = draft with { ClientNickname = Names.Clean(draft.ClientNickname) };
        Session? existing = null;
        if (draft.Id is { } id)
        {
            existing = Data.Sessions.FirstOrDefault(s => s.Id == id);
            if (existing is null)
            {
                return SaveResult<Session>.Failed("session", "Sessionen findes ikke længere.");
            }
        }

        var errors = Rules.ValidateSession(draft, Data);
        if (errors.Count > 0)
        {
            return SaveResult<Session>.Failed(errors);
        }

        var now = _clock.UtcNow;
        var session = existing is null
            ? new Session
            {
                Id = Guid.NewGuid(),
                Date = draft.Date,
                ElementIds = draft.ElementIds.ToList(),
                DurationMinutes = draft.DurationMinutes,
                Price = draft.Price,
                ClientNickname = draft.ClientNickname,
                CreatedAt = now,
                ModifiedAt = now,
            }
            : existing with
            {
                Date = draft.Date,
                ElementIds = draft.ElementIds.ToList(),
                DurationMinutes = draft.DurationMinutes,
                Price = draft.Price,
                ClientNickname = draft.ClientNickname,
                ModifiedAt = now,
            };

        var changes = new ChangeSet();
        if (existing is null || !SameContent(existing, session))
        {
            changes.Put(session);
        }
        else
        {
            session = existing;
        }

        var currentPayments = Data.Payments.Where(p => p.SessionId == session.Id).ToList();
        foreach (var pd in draft.Payments)
        {
            if (pd.Id is { } paymentId)
            {
                var current = currentPayments.FirstOrDefault(p => p.Id == paymentId);
                if (current is null)
                {
                    return SaveResult<Session>.Failed("payments", "En af betalingerne findes ikke længere. Åbn sessionen igen.");
                }

                var updated = current with { Date = pd.Date, Amount = pd.Amount, MethodId = pd.MethodId };
                if (updated != current)
                {
                    changes.Put(updated with { ModifiedAt = now });
                }
            }
            else
            {
                changes.Put(new Payment
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    Date = pd.Date,
                    Amount = pd.Amount,
                    MethodId = pd.MethodId,
                    CreatedAt = now,
                    ModifiedAt = now,
                });
            }
        }

        foreach (var removed in currentPayments.Where(c => draft.Payments.All(pd => pd.Id != c.Id)))
        {
            changes.DeletePayment(removed.Id);
        }

        var notes = new List<string>();
        var others = Data.Sessions.Where(s => s.Id != session.Id).ToList();
        if (existing is null)
        {
            PlanTrips(changes, notes, others, arrival: session.Date, departure: null, now);
        }
        else if (existing.Date != session.Date)
        {
            PlanTrips(changes, notes, others, arrival: session.Date, departure: existing.Date, now);
        }

        await CommitAsync(changes);
        return SaveResult<Session>.Success(session, notes);
    }

    private static bool SameContent(Session a, Session b) =>
        a.Date == b.Date && a.DurationMinutes == b.DurationMinutes && a.Price == b.Price &&
        a.ClientNickname == b.ClientNickname && a.ElementIds.SequenceEqual(b.ElementIds);

    private void PlanTrips(ChangeSet changes, List<string> notes, List<Session> otherSessions, DateOnly? arrival, DateOnly? departure, DateTimeOffset now)
    {
        var route = Data.Settings.ClinicRoute;
        if (arrival is { } a && route.AutoRegisterTrips
            && otherSessions.All(s => s.Date != a)
            && Data.Trips.All(t => !(t.IsClinicTrip && t.Date == a)))
        {
            var trip = TripPlanner.CreateAutomaticTrip(a, Data.Settings, now);
            changes.Put(trip);
            notes.Add(trip.DistanceKm is null
                ? $"Kørsel til klinikken d. {Dk.Date(a)} er registreret. Antal km mangler – angiv afstanden under Indstillinger."
                : $"Kørsel til klinikken d. {Dk.Date(a)} er registreret ({Dk.Km(trip.DistanceKm.Value)}).");
        }

        if (departure is { } d && d != arrival && otherSessions.All(s => s.Date != d)
            && TripPlanner.AutomaticTripOn(d, Data.Trips) is { } orphan)
        {
            changes.DeleteTrip(orphan.Id);
            notes.Add($"Den automatiske kørsel d. {Dk.Date(d)} er fjernet, fordi der ikke længere er en session den dag.");
        }
    }

    public SessionDeletionPreview? PreviewSessionDeletion(Guid sessionId)
    {
        var session = Data.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
        {
            return null;
        }

        var payments = Data.Payments.Where(p => p.SessionId == sessionId).ToList();
        var lastOnDate = Data.Sessions.All(s => s.Id == sessionId || s.Date != session.Date);
        var trip = lastOnDate ? TripPlanner.AutomaticTripOn(session.Date, Data.Trips) : null;
        return new SessionDeletionPreview(session, payments, trip);
    }

    /// <summary>
    /// Deletes a session together with its payments (payments cannot exist without their session).
    /// An automatic, never-edited clinic trip is removed when no other session remains on the date.
    /// Manual trips are never touched.
    /// </summary>
    public async Task<DeletedRecords?> DeleteSessionAsync(Guid sessionId)
    {
        var preview = PreviewSessionDeletion(sessionId);
        if (preview is null)
        {
            return null;
        }

        var changes = new ChangeSet();
        changes.DeleteSession(sessionId);
        foreach (var p in preview.Payments)
        {
            changes.DeletePayment(p.Id);
        }

        var notes = new List<string>();
        if (preview.AutomaticTripRemoved is { } trip)
        {
            changes.DeleteTrip(trip.Id);
            notes.Add($"Den automatiske kørsel d. {Dk.Date(trip.Date)} er også fjernet.");
        }

        await CommitAsync(changes);
        return new DeletedRecords
        {
            Sessions = [preview.Session],
            Payments = preview.Payments,
            Trips = preview.AutomaticTripRemoved is { } t ? [t] : [],
            Notes = notes,
        };
    }

    public async Task<SaveResult<Payment>> AddPaymentAsync(Guid sessionId, PaymentDraft draft)
    {
        var session = Data.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
        {
            return SaveResult<Payment>.Failed("session", "Sessionen findes ikke længere.");
        }

        var errors = Rules.ValidatePayment(draft, session, Data.Payments.Where(p => p.SessionId == sessionId), Data);
        if (errors.Count > 0)
        {
            return SaveResult<Payment>.Failed(errors);
        }

        var now = _clock.UtcNow;
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Date = draft.Date,
            Amount = draft.Amount,
            MethodId = draft.MethodId,
            CreatedAt = now,
            ModifiedAt = now,
        };
        await CommitAsync(new ChangeSet().Put(payment));
        return SaveResult<Payment>.Success(payment);
    }

    public async Task<SaveResult<Payment>> UpdatePaymentAsync(Guid paymentId, PaymentDraft draft)
    {
        var current = Data.Payments.FirstOrDefault(p => p.Id == paymentId);
        var session = current is null ? null : Data.Sessions.FirstOrDefault(s => s.Id == current.SessionId);
        if (current is null || session is null)
        {
            return SaveResult<Payment>.Failed("payment", "Betalingen findes ikke længere.");
        }

        var others = Data.Payments.Where(p => p.SessionId == session.Id && p.Id != paymentId);
        var errors = Rules.ValidatePayment(draft, session, others, Data);
        if (errors.Count > 0)
        {
            return SaveResult<Payment>.Failed(errors);
        }

        var updated = current with { Date = draft.Date, Amount = draft.Amount, MethodId = draft.MethodId, ModifiedAt = _clock.UtcNow };
        await CommitAsync(new ChangeSet().Put(updated));
        return SaveResult<Payment>.Success(updated);
    }

    public async Task<DeletedRecords?> DeletePaymentAsync(Guid paymentId)
    {
        var current = Data.Payments.FirstOrDefault(p => p.Id == paymentId);
        if (current is null)
        {
            return null;
        }

        await CommitAsync(new ChangeSet().DeletePayment(paymentId));
        return new DeletedRecords { Payments = [current] };
    }

    // ---------------------------------------------------------------- Expenses

    public async Task<SaveResult<Expense>> SaveExpenseAsync(ExpenseDraft draft)
    {
        draft = draft with { Description = Names.Clean(draft.Description) };
        Expense? existing = null;
        if (draft.Id is { } id)
        {
            existing = Data.Expenses.FirstOrDefault(e => e.Id == id);
            if (existing is null)
            {
                return SaveResult<Expense>.Failed("expense", "Udgiften findes ikke længere.");
            }
        }

        var errors = Rules.ValidateExpense(draft, Data);
        var changes = new ChangeSet();
        var attachment = existing?.Attachment;
        switch (draft.Attachment)
        {
            case AttachmentChange.RemoveAttachment when attachment is not null:
                changes.DeleteAttachment(attachment.Id);
                attachment = null;
                break;

            case AttachmentChange.ReplaceAttachment replace:
                var problem = AttachmentRules.Check(replace.Data);
                if (problem is not null)
                {
                    errors.Add(new ValidationError("attachment", problem));
                    break;
                }

                var type = AttachmentRules.Detect(replace.Data)!;
                var info = new AttachmentInfo
                {
                    Id = Guid.NewGuid(),
                    FileName = AttachmentRules.SafeFileName(replace.FileName, type),
                    ContentType = type.ContentType,
                    SizeBytes = replace.Data.Length,
                    Sha256 = Hashing.Sha256Hex(replace.Data),
                };
                changes.PutAttachment(new AttachmentBlob(info.Id, info.FileName, info.ContentType, replace.Data));
                if (attachment is not null)
                {
                    changes.DeleteAttachment(attachment.Id);
                }

                attachment = info;
                break;
        }

        if (errors.Count > 0)
        {
            return SaveResult<Expense>.Failed(errors);
        }

        var now = _clock.UtcNow;
        var expense = existing is null
            ? new Expense
            {
                Id = Guid.NewGuid(),
                Date = draft.Date,
                Amount = draft.Amount,
                CategoryId = draft.CategoryId,
                Description = draft.Description,
                Attachment = attachment,
                CreatedAt = now,
                ModifiedAt = now,
            }
            : existing with
            {
                Date = draft.Date,
                Amount = draft.Amount,
                CategoryId = draft.CategoryId,
                Description = draft.Description,
                Attachment = attachment,
                ModifiedAt = now,
            };

        if (existing is not null && changes.IsEmpty && existing with { ModifiedAt = now } == expense)
        {
            return SaveResult<Expense>.Success(existing);
        }

        changes.Put(expense);
        await CommitAsync(changes);
        return SaveResult<Expense>.Success(expense);
    }

    public async Task<DeletedRecords?> DeleteExpenseAsync(Guid expenseId)
    {
        var expense = Data.Expenses.FirstOrDefault(e => e.Id == expenseId);
        if (expense is null)
        {
            return null;
        }

        AttachmentBlob? blob = null;
        var changes = new ChangeSet().DeleteExpense(expenseId);
        if (expense.Attachment is { } info)
        {
            // Keep the bytes in memory so the deletion can be undone.
            try
            {
                blob = await _store.GetAttachmentAsync(info.Id);
            }
            catch (Exception)
            {
                blob = null;
            }

            changes.DeleteAttachment(info.Id);
        }

        await CommitAsync(changes);
        return new DeletedRecords { Expenses = [expense], Attachment = blob };
    }

    public async Task<AttachmentBlob?> GetAttachmentAsync(Guid attachmentId)
    {
        try
        {
            return await _store.GetAttachmentAsync(attachmentId);
        }
        catch (Exception ex) when (ex is not StorageException)
        {
            throw new StorageException("Kvitteringen kunne ikke hentes.", ex);
        }
    }

    // ---------------------------------------------------------------- Trips

    public async Task<SaveResult<Trip>> SaveTripAsync(TripDraft draft)
    {
        draft = draft with
        {
            Purpose = Names.Clean(draft.Purpose) ?? string.Empty,
            FromLabel = Names.Clean(draft.FromLabel) ?? string.Empty,
            ToLabel = Names.Clean(draft.ToLabel) ?? string.Empty,
        };

        Trip? existing = null;
        if (draft.Id is { } id)
        {
            existing = Data.Trips.FirstOrDefault(t => t.Id == id);
            if (existing is null)
            {
                return SaveResult<Trip>.Failed("trip", "Kørslen findes ikke længere.");
            }
        }

        var errors = Rules.ValidateTrip(draft, Data.Trips);
        if (errors.Count > 0)
        {
            return SaveResult<Trip>.Failed(errors);
        }

        var now = _clock.UtcNow;
        var trip = new Trip
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Date = draft.Date,
            Purpose = draft.Purpose,
            FromLabel = draft.FromLabel,
            ToLabel = draft.ToLabel,
            DistanceKm = draft.DistanceKm,
            IncludeInTaxReport = draft.IncludeInTaxReport,
            IsClinicTrip = draft.IsClinicTrip,
            Origin = TripOrigin.Manual, // once Sofie has edited a trip it is hers and never removed automatically
            CreatedAt = existing?.CreatedAt ?? now,
            ModifiedAt = now,
        };

        if (existing is not null && existing with { Origin = TripOrigin.Manual, ModifiedAt = now } == trip)
        {
            return SaveResult<Trip>.Success(existing);
        }

        await CommitAsync(new ChangeSet().Put(trip));
        return SaveResult<Trip>.Success(trip);
    }

    public async Task<DeletedRecords?> DeleteTripAsync(Guid tripId)
    {
        var trip = Data.Trips.FirstOrDefault(t => t.Id == tripId);
        if (trip is null)
        {
            return null;
        }

        await CommitAsync(new ChangeSet().DeleteTrip(tripId));
        return new DeletedRecords { Trips = [trip] };
    }

    /// <summary>Fills in the configured distance on clinic trips that were registered before the distance was known.</summary>
    public async Task<int> FillMissingClinicTripDistancesAsync()
    {
        if (Data.Settings.ClinicRoute.RoundTripKm is not { } km)
        {
            return 0;
        }

        var now = _clock.UtcNow;
        var changes = new ChangeSet();
        var missing = Data.Trips.Where(t => t.IsClinicTrip && t.DistanceKm is null).ToList();
        foreach (var t in missing)
        {
            changes.Put(t with { DistanceKm = km, ModifiedAt = now });
        }

        await CommitAsync(changes);
        return missing.Count;
    }

    // ---------------------------------------------------------------- Undo

    /// <summary>Puts back records removed by a delete. Never creates a second clinic trip on a date.</summary>
    public async Task UndoDeleteAsync(DeletedRecords deleted)
    {
        var changes = new ChangeSet();
        foreach (var s in deleted.Sessions)
        {
            changes.Put(s);
        }

        foreach (var p in deleted.Payments.Where(p => Data.Sessions.Any(s => s.Id == p.SessionId) || deleted.Sessions.Any(s => s.Id == p.SessionId)))
        {
            changes.Put(p);
        }

        foreach (var e in deleted.Expenses)
        {
            var attachment = e.Attachment;
            if (attachment is not null)
            {
                if (deleted.Attachment is { } blob && blob.Id == attachment.Id)
                {
                    changes.PutAttachment(blob);
                }
                else
                {
                    attachment = null; // bytes could not be recovered
                }
            }

            changes.Put(e with { Attachment = attachment });
        }

        foreach (var t in deleted.Trips)
        {
            if (!t.IsClinicTrip || Data.Trips.All(x => !(x.IsClinicTrip && x.Date == t.Date)))
            {
                changes.Put(t);
            }
        }

        await CommitAsync(changes);
    }

    // ---------------------------------------------------------------- Lookups

    public IReadOnlyList<LookupItem> ActiveLookups(LookupKind kind) =>
        Data.Lookups(kind).Where(l => !l.IsArchived).OrderBy(l => l.SortOrder).ThenBy(l => l.Name, DanishComparer.Instance).ToList();

    /// <summary>Adds a new value, or returns the existing one when the name only differs by case or spacing.</summary>
    public async Task<SaveResult<LookupItem>> AddLookupAsync(LookupKind kind, string name)
    {
        var clean = Names.Clean(name);
        if (clean is null)
        {
            return SaveResult<LookupItem>.Failed("name", "Skriv et navn.");
        }

        if (clean.Length > Names.MaxLookupLength)
        {
            return SaveResult<LookupItem>.Failed("name", $"Navnet må højst være {Names.MaxLookupLength} tegn.");
        }

        var items = Data.Lookups(kind);
        if (items.FirstOrDefault(l => Names.SameName(l.Name, clean)) is { } existing)
        {
            if (!existing.IsArchived)
            {
                return SaveResult<LookupItem>.Success(existing);
            }

            var restored = existing with { IsArchived = false };
            await CommitAsync(new ChangeSet().Put(kind, restored));
            return SaveResult<LookupItem>.Success(restored);
        }

        var item = new LookupItem
        {
            Id = Guid.NewGuid(),
            Name = clean,
            SortOrder = items.Count == 0 ? 0 : items.Max(i => i.SortOrder) + 1,
            CreatedAt = _clock.UtcNow,
        };
        await CommitAsync(new ChangeSet().Put(kind, item));
        return SaveResult<LookupItem>.Success(item);
    }

    public async Task<SaveResult<LookupItem>> RenameLookupAsync(LookupKind kind, Guid id, string name)
    {
        var item = Data.Lookups(kind).FirstOrDefault(l => l.Id == id);
        if (item is null)
        {
            return SaveResult<LookupItem>.Failed("name", "Findes ikke længere.");
        }

        var clean = Names.Clean(name);
        if (clean is null)
        {
            return SaveResult<LookupItem>.Failed("name", "Skriv et navn.");
        }

        if (clean.Length > Names.MaxLookupLength)
        {
            return SaveResult<LookupItem>.Failed("name", $"Navnet må højst være {Names.MaxLookupLength} tegn.");
        }

        if (Data.Lookups(kind).Any(l => l.Id != id && Names.SameName(l.Name, clean)))
        {
            return SaveResult<LookupItem>.Failed("name", $"\"{clean}\" findes allerede.");
        }

        var renamed = item with { Name = clean };
        if (renamed != item)
        {
            await CommitAsync(new ChangeSet().Put(kind, renamed));
        }

        return SaveResult<LookupItem>.Success(renamed);
    }

    public async Task SetLookupArchivedAsync(LookupKind kind, Guid id, bool archived)
    {
        var item = Data.Lookups(kind).FirstOrDefault(l => l.Id == id);
        if (item is null || item.IsArchived == archived)
        {
            return;
        }

        await CommitAsync(new ChangeSet().Put(kind, item with { IsArchived = archived }));
    }

    public int UsageCount(LookupKind kind, Guid id) => kind switch
    {
        LookupKind.SessionElement => Data.Sessions.Count(s => s.ElementIds.Contains(id)),
        LookupKind.ExpenseCategory => Data.Expenses.Count(e => e.CategoryId == id),
        LookupKind.PaymentMethod => Data.Payments.Count(p => p.MethodId == id),
        _ => 0,
    };

    // ---------------------------------------------------------------- Settings and backup bookkeeping

    public async Task<SaveResult<AppSettings>> UpdateSettingsAsync(AppSettings settings)
    {
        var errors = new List<ValidationError>();
        if (Rules.CheckRouteKm(settings.ClinicRoute.RoundTripKm) is { } kmError)
        {
            errors.Add(new("km", kmError));
        }

        if (settings.Locations.Any(l => Names.Clean(l.Name) is null))
        {
            errors.Add(new("location", "Alle steder skal have et navn."));
        }

        if (settings.FindLocation(settings.ClinicRoute.FromLocationId) is null || settings.FindLocation(settings.ClinicRoute.ToLocationId) is null)
        {
            errors.Add(new("route", "Ruten henviser til et sted, der ikke findes."));
        }

        if (errors.Count > 0)
        {
            return SaveResult<AppSettings>.Failed(errors);
        }

        settings = settings with
        {
            Locations = settings.Locations.Select(l => l with { Name = Names.Clean(l.Name)!, Address = Names.Clean(l.Address) ?? string.Empty }).ToList(),
        };
        await CommitAsync(new ChangeSet().Put(settings));
        return SaveResult<AppSettings>.Success(settings);
    }

    public async Task UpdateBackupStatusAsync(Func<BackupStatus, BackupStatus> update)
    {
        var updated = update(BackupStatus);
        if (updated == BackupStatus)
        {
            return;
        }

        await CommitAsync(new ChangeSet { CountsAsDataChange = false }.Put(updated));
    }

    // ---------------------------------------------------------------- Restore

    /// <summary>
    /// Replaces the complete dataset (never a merge). Attachments are staged first; the switch to the
    /// new records happens in a single atomic transaction. If anything fails, the current data stays intact.
    /// </summary>
    public async Task ReplaceAllAsync(AppData data, IReadOnlyCollection<AttachmentBlob> attachments, DateTimeOffset sourceCreatedAt)
    {
        var errors = DataValidator.Validate(data, attachments.Select(a => a.Id).ToHashSet());
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        await _writeGate.WaitAsync();
        try
        {
            var referenced = ReferencedAttachmentIds(data);
            foreach (var blob in attachments.Where(a => referenced.Contains(a.Id)))
            {
                await _store.PutAttachmentAsync(blob);
            }

            var status = new BackupStatus
            {
                TrackingStartedAt = _clock.UtcNow,
                LastDataChangeAt = sourceCreatedAt,
                LastConfirmedAt = sourceCreatedAt,
            };
            await _store.ReplaceAllAsync(DataSerializer.SerializeAllStores(data, status), referenced);
            Data = data;
            BackupStatus = status;
        }
        catch (Exception ex) when (ex is not StorageException)
        {
            throw new StorageException("Gendannelsen mislykkedes. Dine nuværende data er ikke ændret.", ex);
        }
        finally
        {
            _writeGate.Release();
        }

        Changed?.Invoke();
    }

    public async Task<int> TryCleanUpOrphanAttachmentsAsync()
    {
        try
        {
            var referenced = ReferencedAttachmentIds(Data);
            var orphans = (await _store.ListAttachmentIdsAsync()).Where(id => !referenced.Contains(id)).ToList();
            if (orphans.Count > 0)
            {
                await _store.DeleteAttachmentsAsync(orphans);
            }

            return orphans.Count;
        }
        catch (Exception)
        {
            return 0; // best effort only
        }
    }

    // ---------------------------------------------------------------- Commit

    private async Task CommitAsync(ChangeSet changes)
    {
        if (changes.IsEmpty)
        {
            return;
        }

        await _writeGate.WaitAsync();
        try
        {
            if (changes.CountsAsDataChange)
            {
                var now = _clock.UtcNow;
                changes.Put(BackupStatus with { LastDataChangeAt = now, TrackingStartedAt = BackupStatus.TrackingStartedAt ?? now });
            }

            try
            {
                await _store.CommitAsync(DataSerializer.SerializeOperations(changes), changes.AttachmentPut);
            }
            catch (Exception ex) when (ex is not StorageException)
            {
                throw new StorageException("Det kunne ikke gemmes. Prøv igen.", ex);
            }

            Data = Apply(Data, changes);
            BackupStatus = changes.Changes.Select(c => c.Value).OfType<BackupStatus>().LastOrDefault() ?? BackupStatus;
        }
        finally
        {
            _writeGate.Release();
        }

        Changed?.Invoke();
    }

    internal static AppData Apply(AppData data, ChangeSet changes) => data with
    {
        Settings = changes.Changes.Select(c => c.Value).OfType<AppSettings>().LastOrDefault() ?? data.Settings,
        Sessions = ApplyList(data.Sessions, StoreNames.Sessions, changes, s => s.Id),
        Payments = ApplyList(data.Payments, StoreNames.Payments, changes, p => p.Id),
        Expenses = ApplyList(data.Expenses, StoreNames.Expenses, changes, e => e.Id),
        Trips = ApplyList(data.Trips, StoreNames.Trips, changes, t => t.Id),
        ElementTypes = ApplyList(data.ElementTypes, StoreNames.ElementTypes, changes, l => l.Id),
        ExpenseCategories = ApplyList(data.ExpenseCategories, StoreNames.ExpenseCategories, changes, l => l.Id),
        PaymentMethods = ApplyList(data.PaymentMethods, StoreNames.PaymentMethods, changes, l => l.Id),
    };

    private static IReadOnlyList<T> ApplyList<T>(IReadOnlyList<T> list, string store, ChangeSet changes, Func<T, Guid> key)
    {
        var relevant = changes.Changes.Where(c => c.Store == store).ToList();
        if (relevant.Count == 0)
        {
            return list;
        }

        var keys = relevant.Select(c => c.Key).ToHashSet();
        var result = new List<T>(list.Count + relevant.Count);
        var replaced = new HashSet<string>();
        foreach (var item in list)
        {
            var k = key(item).ToString();
            if (!keys.Contains(k))
            {
                result.Add(item);
                continue;
            }

            // Keep the position of updated records stable.
            var change = relevant.First(c => c.Key == k);
            if (!change.IsDelete)
            {
                result.Add((T)change.Value!);
            }

            replaced.Add(k);
        }

        result.AddRange(relevant.Where(c => !c.IsDelete && !replaced.Contains(c.Key)).Select(c => (T)c.Value!));
        return result;
    }
}
