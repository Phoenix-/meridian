using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Services;

namespace Meridian.ViewModels;

// Backs the settings window. Each property is a thin view over SettingsStore:
// the getter reads the persisted value, the setter writes it straight through
// (the store persists and raises Changed, which drives live application). No
// dirty-state or Apply button — toggles take effect immediately, matching the
// Windows settings convention.
//
// Subscribes to SettingsStore.Changed so the bound toggles also reflect a
// change made elsewhere (today only the window itself writes, but this keeps
// the UI honest if another path ever mutates the store while it's open).
// Disposed by the window on Closed to drop that subscription.
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    public SettingsViewModel()
    {
        SettingsStore.Changed += OnStoreChanged;
    }

    public bool FlashTaskbarOnReminder
    {
        get => SettingsStore.FlashTaskbarOnReminder;
        set
        {
            if (SettingsStore.FlashTaskbarOnReminder == value) return;
            SettingsStore.FlashTaskbarOnReminder = value;
            OnPropertyChanged();
        }
    }

    public bool ShowOngoingEventOverlay
    {
        get => SettingsStore.ShowOngoingEventOverlay;
        set
        {
            if (SettingsStore.ShowOngoingEventOverlay == value) return;
            SettingsStore.ShowOngoingEventOverlay = value;
            OnPropertyChanged();
        }
    }

    public bool SuppressAllPopups
    {
        get => SettingsStore.SuppressAllPopups;
        set
        {
            if (SettingsStore.SuppressAllPopups == value) return;
            SettingsStore.SuppressAllPopups = value;
            OnPropertyChanged();
        }
    }

    public bool RegisterForNotifications
    {
        get => SettingsStore.RegisterForNotifications;
        set
        {
            if (SettingsStore.RegisterForNotifications == value) return;
            SettingsStore.RegisterForNotifications = value;
            OnPropertyChanged();
        }
    }

    public bool DebugFeaturesEnabled
    {
        get => SettingsStore.DebugFeaturesEnabled;
        set
        {
            if (SettingsStore.DebugFeaturesEnabled == value) return;
            SettingsStore.DebugFeaturesEnabled = value;
            OnPropertyChanged();
        }
    }

    // Re-raise as a property change so the bound ToggleSwitch refreshes. The
    // store's property name matches ours 1:1, so forward it verbatim; a value
    // we wrote ourselves just re-raises the same property harmlessly.
    private void OnStoreChanged(string name) => OnPropertyChanged(name);

    public void Dispose() => SettingsStore.Changed -= OnStoreChanged;
}
