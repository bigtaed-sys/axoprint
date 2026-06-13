using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AxoPrint.Agent;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.Save();
    }

    // Closing the window hides it to the tray instead of quitting the agent.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
