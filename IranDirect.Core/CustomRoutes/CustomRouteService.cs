namespace IranDirect.Core.CustomRoutes;

public sealed class CustomRouteService
{
    private readonly ICustomRouteRepository _repository;
    private readonly CustomRouteEntryValidator _validator;
    private readonly TimeProvider _timeProvider;

    public CustomRouteService(
        ICustomRouteRepository repository,
        CustomRouteEntryValidator validator,
        TimeProvider? timeProvider = null)
    {
        _repository = repository;
        _validator = validator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<CustomRouteEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.GetAllAsync(cancellationToken);
    }

    public async Task<CustomRouteEntry> AddAsync(
        CustomRouteEntryType type,
        string? value,
        string? description = null,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        string normalized =
            NormalizeValueOrThrow(type, value);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CustomRouteEntry? added = null;

        await _repository.MutateAsync(collection =>
        {
            if (ContainsDuplicate(
                    collection,
                    type,
                    normalized,
                    excludeId: null))
            {
                throw new InvalidOperationException(
                    "Duplicate custom route.");
            }

            added = new CustomRouteEntry
            {
                Id = Guid.NewGuid(),
                Type = type,
                Value = normalized,
                Enabled = enabled,
                Description = description,
                CreatedAt = now,
                ModifiedAt = now
            };

            return collection with
            {
                Entries =
                    collection.Entries.Append(added).ToArray()
            };
        }, cancellationToken);

        return added!;
    }

    public async Task<CustomRouteEntry> SetEnabledAsync(
        Guid id,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        CustomRouteEntry? updated = null;

        await _repository.MutateAsync(collection =>
        {
            int index = FindIndex(collection, id);

            if (index < 0)
            {
                throw new KeyNotFoundException(
                    $"Custom route '{id}' was not found.");
            }

            CustomRouteEntry entry = collection.Entries[index];

            if (entry.Enabled == enabled)
            {
                updated = entry;
                return collection;
            }

            updated = entry with
            {
                Enabled = enabled,
                ModifiedAt = _timeProvider.GetUtcNow()
            };

            return Replace(collection, index, updated);
        }, cancellationToken);

        return updated!;
    }

    public async Task<CustomRouteEntry> UpdateAsync(
        Guid id,
        string? value,
        string? description,
        CancellationToken cancellationToken = default)
    {
        CustomRouteEntry? updated = null;

        await _repository.MutateAsync(collection =>
        {
            int index = FindIndex(collection, id);

            if (index < 0)
            {
                throw new KeyNotFoundException(
                    $"Custom route '{id}' was not found.");
            }

            CustomRouteEntry entry = collection.Entries[index];

            string? newValue = null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                newValue =
                    NormalizeValueOrThrow(entry.Type, value);

                if (ContainsDuplicate(
                        collection,
                        entry.Type,
                        newValue,
                        excludeId: id))
                {
                    throw new InvalidOperationException(
                        "Duplicate custom route.");
                }
            }

            bool valueChanged =
                newValue is not null
                && !newValue.Equals(
                    entry.Value,
                    StringComparison.Ordinal);

            bool descriptionChanged =
                description is not null
                && description != entry.Description;

            if (!valueChanged && !descriptionChanged)
            {
                updated = entry;
                return collection;
            }

            updated = entry with
            {
                Value = newValue ?? entry.Value,
                Description =
                    description ?? entry.Description,
                ModifiedAt = _timeProvider.GetUtcNow()
            };

            return Replace(collection, index, updated);
        }, cancellationToken);

        return updated!;
    }

    public async Task<bool> RemoveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        bool removed = false;

        await _repository.MutateAsync(collection =>
        {
            int index = FindIndex(collection, id);

            if (index < 0)
            {
                return collection;
            }

            removed = true;

            return collection with
            {
                Entries = collection.Entries
                    .Where((_, i) => i != index)
                    .ToArray()
            };
        }, cancellationToken);

        return removed;
    }

    private string NormalizeValueOrThrow(
        CustomRouteEntryType type,
        string? value)
    {
        CustomRouteValidationResult result =
            _validator.ValidateAndNormalize(type, value);

        if (!result.IsValid)
        {
            throw new ArgumentException(
                string.Join(" ", result.Errors),
                nameof(value));
        }

        return result.NormalizedValue!;
    }

    private static bool ContainsDuplicate(
        CustomRouteCollection collection,
        CustomRouteEntryType type,
        string normalizedValue,
        Guid? excludeId)
    {
        return collection.Entries.Any(entry =>
            entry.Id != excludeId
            && entry.Type == type
            && entry.Value.Equals(
                normalizedValue,
                StringComparison.OrdinalIgnoreCase));
    }

    private static int FindIndex(
        CustomRouteCollection collection,
        Guid id)
    {
        for (int i = 0; i < collection.Entries.Count; i++)
        {
            if (collection.Entries[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static CustomRouteCollection Replace(
        CustomRouteCollection collection,
        int index,
        CustomRouteEntry entry)
    {
        return collection with
        {
            Entries = collection.Entries
                .Select((e, i) => i == index ? entry : e)
                .ToArray()
        };
    }
}
