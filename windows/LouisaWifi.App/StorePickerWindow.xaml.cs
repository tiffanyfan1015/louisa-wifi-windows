using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LouisaWifi.Core;

namespace LouisaWifi.App;

public partial class StorePickerWindow : Window
{
    private readonly StoreRepository stores;
    private readonly Func<Store, string?, Task> connect;
    private readonly Action cancel;
    private bool connecting;
    public StorePickerWindow(StoreRepository stores, Func<Store, string?, Task> connect, Action cancel)
    {
        this.stores = stores; this.connect = connect; this.cancel = cancel;
        InitializeComponent();
        StoreList.ItemsSource = stores.All;
        ResultCount.Text = $"{stores.All.Count} 家門市";
        Candidates.ItemsSource = new[] { "自動依序嘗試" };
        Candidates.SelectedIndex = 0;
        Status.Text = "請選擇門市";
    }
    public void SetStatus(string status)
    {
        if (Status.Text != status) Status.Text = status;
        CancelButton.IsEnabled = connecting || status.StartsWith("正在掃描") || status.StartsWith("正在連線");
    }
    internal void PreparePreview()
    {
        Search.Text = "台北";
        StoreList.SelectedIndex = 0;
        Status.Text = "自動連線已暫停";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (StoreList is null) return;
        var results = stores.Search(Search.Text).ToArray();
        StoreList.ItemsSource = results;
        ResultCount.Text = $"{results.Length} 家門市";
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = results.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Candidates is null) return;
        Candidates.ItemsSource = new[] { "自動依序嘗試" }.Concat((StoreList.SelectedItem as Store)?.Passwords ?? []).ToArray();
        Candidates.SelectedIndex = 0;
        Password.Clear();
        var store = StoreList.SelectedItem as Store;
        SelectedName.Text = store?.Name ?? "選擇門市";
        SelectedAddress.Text = store?.Address ?? "";
        SelectedPhone.Text = store is null ? "" : $"電話  {store.Phone}";
        ConnectButton.IsEnabled = store is not null && !connecting;
        Candidates.IsEnabled = Password.IsEnabled = store is not null && !connecting;
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => cancel();
    private async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (StoreList.SelectedItem is not Store store) { Status.Text = "請先選擇門市"; return; }
        string? password = Password.Password.Length > 0 ? Password.Password
            : Candidates.SelectedIndex > 0 ? Candidates.SelectedItem as string : null;
        if (password is not null && !CandidateSelector.ValidPassword(password)) { Status.Text = "密碼需為 8–63 個英數／符號字元"; return; }
        ConnectButton.IsEnabled = false;
        connecting = true;
        ConnectButton.Content = "連線中…";
        CancelButton.IsEnabled = true;
        Search.IsEnabled = StoreList.IsEnabled = Candidates.IsEnabled = Password.IsEnabled = false;
        try { await connect(store, password); }
        catch (Exception ex) { Status.Text = $"操作失敗（{ex.GetType().Name}）"; }
        finally
        {
            connecting = false;
            ConnectButton.Content = "連線";
            ConnectButton.IsEnabled = StoreList.SelectedItem is Store;
            Search.IsEnabled = StoreList.IsEnabled = true;
            Candidates.IsEnabled = Password.IsEnabled = StoreList.SelectedItem is Store;
            CancelButton.IsEnabled = false;
            Password.Clear();
        }
    }
}
