// ============================================================
// DevPilot — IT Automation Hub
// AppLauncherView.xaml.cs — Code-behind for App Launcher tab
// Author: Bogdan Murar (Z003FU5C) — Siemens AG
// ============================================================

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevPilot.Models;
using DevPilot.UI.ViewModels;
using DevPilot.UI.Views.Dialogs;

namespace DevPilot.UI.Views;

public partial class AppLauncherView : UserControl
{
    public AppLauncherView()
    {
        InitializeComponent();
        // Wire ShowAddEditDialog dupa ce DataContext e setat
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is AppLauncherViewModel vm)
        {
            vm.ShowAddEditDialog = async (existing) =>
            {
                var dlg = new AddAppDialog(existing) { Owner = Window.GetWindow(this) };
                dlg.ShowDialog();
                return dlg.Result;
            };
        }
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AppLauncherViewModel vm && vm.LaunchCommand.CanExecute(null))
            vm.LaunchCommand.Execute(null);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }
}