using Meridian.Models;

namespace Meridian.Services;

// Tasks are persisted as one blob: account-email -> list. No incremental sync
// in the current iteration; refresh replaces the snapshot wholesale.
public interface ITaskStore
{
    TaskStoreData? Load();

    void Save(TaskStoreData data);
}
