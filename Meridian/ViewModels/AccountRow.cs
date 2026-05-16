using CommunityToolkit.Mvvm.ComponentModel;
using Meridian.Auth;

namespace Meridian.ViewModels;

// Lightweight wrapper used by the accounts flyout so we can flip an
// IsExpired flag per row without rebuilding the entire list. Bound from
// XAML via x:Bind on AccountRow members (x:DataType="vm:AccountRow").
//
// Plain AccountId is a record struct, so it can't carry observable state on
// its own — and we don't want to leak UI concerns onto the auth model.
public partial class AccountRow(AccountId id) : ObservableObject
{
    public AccountId Id { get; } = id;
    public string Email => Id.Email;

    [ObservableProperty]
    private bool _isExpired;
}
