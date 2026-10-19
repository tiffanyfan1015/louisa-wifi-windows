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
    public StorePickerWindow(StoreRepository stores, bool authorized, Func<Store, string?, Task> connect, Action cancel)
    {
        this.stores = stores; this.connect = connect; this.cancel = cancel;
        InitializeComponent();
        Authorized.IsChecked = authorized;
        StoreList.ItemsSource = stores.All;
        Status.Text = "搜尋門市名稱或地址。已有 Wi-Fi 連線時會保留現有連線。";
    }
    public void SetStatus(string status) => Status.Text = status;
    internal void PreparePreview()
    {
        Search.Text = "台北";
        StoreList.SelectedIndex = 0;
        Status.Text = "自動連線已暫停。已有 Wi-Fi 連線時會保留現有連線。";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e)
    { if (StoreList is not null) StoreList.ItemsSource = stores.Search(Search.Text).ToArray(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Candidates is null) return;
        Candidates.ItemsSource = new[] { "自動依序嘗試" }.Concat((StoreList.SelectedItem as Store)?.Passwords ?? []).ToArray();
        Candidates.SelectedIndex = 0;
        Password.Clear();
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => cancel();
    private async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (StoreList.SelectedItem is not Store store) { Status.Text = "請先選擇門市"; return; }
        if (Authorized.IsChecked != true) { Status.Text = "請先確認已獲准使用門市 Wi-Fi"; return; }
        string? password = Password.Password.Length > 0 ? Password.Password
            : Candidates.SelectedIndex > 0 ? Candidates.SelectedItem as string : null;
        if (password is not null && !CandidateSelector.ValidPassword(password)) { Status.Text = "密碼需為 8–63 個英數／符號字元"; return; }
        ConnectButton.IsEnabled = false;
        try { await connect(store, password); }
        catch (Exception ex) { Status.Text = $"操作失敗（{ex.GetType().Name}）"; }
        finally { ConnectButton.IsEnabled = true; Password.Clear(); }
    }
}
