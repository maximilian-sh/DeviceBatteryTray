using System.Windows;

namespace LGSTrayUI
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow(string version)
        {
            InitializeComponent();
            DataContext = new UpdateProgressViewModel(version);
        }

        public void UpdateProgress(int percent, string status = null)
        {
            if (DataContext is UpdateProgressViewModel vm)
            {
                vm.Progress = percent;
                if (!string.IsNullOrEmpty(status))
                {
                    vm.StatusText = status;
                }
            }
        }

        public void SetStatus(string status)
        {
            if (DataContext is UpdateProgressViewModel vm)
            {
                vm.StatusText = status;
            }
        }
    }

    public class UpdateProgressViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _statusText = "Downloading update...";
        private string _versionText;
        private int _progress = 0;

        public UpdateProgressViewModel(string version)
        {
            _versionText = $"Version {version}";
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string VersionText
        {
            get => _versionText;
            set
            {
                _versionText = value;
                OnPropertyChanged(nameof(VersionText));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public string ProgressText => $"{Progress}%";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

