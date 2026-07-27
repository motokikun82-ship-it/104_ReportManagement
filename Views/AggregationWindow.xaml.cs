using System.Windows;
using System.Windows.Controls;
using ReportManagement.ViewModels;

namespace ReportManagement.Views;

public partial class AggregationWindow : Window
{
    public AggregationWindow()
    {
        InitializeComponent();
        DataContext = new AggregationViewModel();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is AggregationViewModel vm)
            vm.SaveState();
    }

    private void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName.StartsWith("Col") && DataContext is AggregationViewModel vm)
        {
            var displayName = vm.GetPeriodDisplayName(e.PropertyName);
            if (displayName != null)
                e.Column.Header = displayName;
        }
    }
}
