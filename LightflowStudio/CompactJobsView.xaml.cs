using System.Windows;

namespace LightflowStudio;

/// <summary>Retained compact Jobs presentation. The shell owns scheduler subscriptions and commands.</summary>
public partial class CompactJobsView : System.Windows.Controls.UserControl
{
    private readonly MainWindow _owner;
    internal CompactJobsView(MainWindow owner)
    {
        _owner = owner;
        Resources = owner.Resources;
        InitializeComponent();
    }

    private void ShowAllJobs_Click(object sender, RoutedEventArgs e) => _owner.JobsStatus_Click(sender, e);
    private void MaximumExports_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => _owner.MaximumExports_SelectionChanged(sender, e);
    private void JobsQueueGate_Click(object sender, RoutedEventArgs e) => _owner.JobsQueueGate_Click(sender, e);
    private void JobsCancelAll_Click(object sender, RoutedEventArgs e) => _owner.JobsCancelAll_Click(sender, e);
    private void JobExpansionToggle_Click(object sender, RoutedEventArgs e) => _owner.JobExpansionToggle_Click(sender, e);
    private void JobsMoveUp_Click(object sender, RoutedEventArgs e) => _owner.JobsMoveUp_Click(sender, e);
    private void JobsMoveDown_Click(object sender, RoutedEventArgs e) => _owner.JobsMoveDown_Click(sender, e);
    private void JobsPause_Click(object sender, RoutedEventArgs e) => _owner.JobsPause_Click(sender, e);
    private void JobsResume_Click(object sender, RoutedEventArgs e) => _owner.JobsResume_Click(sender, e);
    private void JobsRetry_Click(object sender, RoutedEventArgs e) => _owner.JobsRetry_Click(sender, e);
    private void JobsCancel_Click(object sender, RoutedEventArgs e) => _owner.JobsCancel_Click(sender, e);
}
