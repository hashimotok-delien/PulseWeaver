using System.Windows;
using System.Windows.Controls;

namespace PulseWeaver;

internal partial class ProcessPickerWindow : Window
{
    public ProcessPickerWindow(IReadOnlyList<AudioProcessCandidate> candidates)
    {
        InitializeComponent();
        CandidateList.ItemsSource = candidates;
        if (candidates.Count > 0)
        {
            CandidateList.SelectedIndex = 0;
        }

        CandidateList.MouseDoubleClick += (_, _) => Accept();
        CandidateList.SelectionChanged += (_, _) => SelectButton.IsEnabled = CandidateList.SelectedItem is not null;
        SelectButton.IsEnabled = candidates.Count > 0;
    }

    public AudioProcessCandidate? SelectedCandidate { get; private set; }

    public bool IncludeChildProcesses => IncludeTreeCheckBox.IsChecked == true;

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Accept()
    {
        if (CandidateList.SelectedItem is not AudioProcessCandidate candidate)
        {
            return;
        }

        SelectedCandidate = candidate;
        DialogResult = true;
    }
}
