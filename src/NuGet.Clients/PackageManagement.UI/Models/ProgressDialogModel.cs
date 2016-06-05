using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NuGet.PackageManagement.UI
{
    internal class ProgressDialogModel : INotifyPropertyChanged
    {
        private string _waitMessage;
        private string _progressText;
        private bool _isCancelable;

        public ProgressDialogModel(string caption, ProgressDialogData initialData)
        {
            Caption = caption;
            _waitMessage = initialData.WaitMessage;
            _progressText = initialData.ProgressText;
            _isCancelable = initialData.IsCancelable;
        }

        public string Caption { get; }

        public string WaitMessage
        {
            get { return _waitMessage; }
            set { _waitMessage = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get { return _progressText; }
            set { _progressText = value; OnPropertyChanged(); }
        }

        public bool IsCancelable
        {
            get { return _isCancelable; }
            set { _isCancelable = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
