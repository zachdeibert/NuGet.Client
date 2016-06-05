using System;
using System.Threading;
using NuGet.ProjectManagement;

#if !STANDALONE
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
#endif

namespace NuGet.PackageManagement.UI
{
    public static class ProgressDialog
    {
        public static DialogSession Start(string caption, ProgressDialogData initialData, INuGetUI uiService)
        {
#if STANDALONE
            // Simply output progress changes to console
            return new DialogSession(caption, initialData, uiService);
#else
            var waitForDialogFactory = (IVsThreadedWaitDialogFactory)Package.GetGlobalService(typeof(SVsThreadedWaitDialogFactory));
            var progressData = new ThreadedWaitDialogProgressData(
                initialData.WaitMessage,
                initialData.ProgressText,
                null,
                initialData.IsCancelable,
                initialData.CurrentStep,
                initialData.TotalSteps);
            ThreadedWaitDialogHelper.Session session = waitForDialogFactory.StartWaitDialog(caption, progressData);
            return new DialogSession(session);
#endif
        }
    }

    public class DialogSession : IDisposable
    {
#if STANDALONE

        private INuGetProjectContext _logger;
        private string _caption;

        public DialogSession(string caption, ProgressDialogData initialData, INuGetUI uiService)
        {
            _caption = caption;
            _logger = uiService.ProgressWindow;
            UserCancellationToken = CancellationToken.None;
            Progress = new DialogProgress(caption, _logger);

            _logger.Log(MessageLevel.Info, $"Progress dialog '{caption}' opening.");
            _logger.Log(MessageLevel.Info, $"Progress dialog '{caption}': {initialData.ProgressText}: {initialData.WaitMessage}");
        }

        public void Dispose()
        {
            _logger.Log(MessageLevel.Info, $"Progress dialog '{_caption}': closing.");
        }
#else
        private readonly Action _dispose;

        public DialogSession(ThreadedWaitDialogHelper.Session progress)
        {
            UserCancellationToken = progress.UserCancellationToken;
            _dispose = progress.Dispose;
            Progress = new DialogProgress(progress.Progress);
        }

        public void Dispose()
        {
            _dispose();
        }
#endif

        public IProgress<ProgressDialogData> Progress { get; }

        public CancellationToken UserCancellationToken { get; }
    }

    public class DialogProgress : IProgress<ProgressDialogData>
    {
        private readonly string _caption;
        private readonly INuGetProjectContext _logger;

        public DialogProgress(string caption, INuGetProjectContext logger)
        {
            _caption = caption;
            _logger = logger;
        }

#if STANDALONE
        public void Report(ProgressDialogData value)
        {
            _logger.Log(MessageLevel.Info, $"Progress dialog '{_caption}': {value.ProgressText}: {value.WaitMessage}");
        }
#else
        private readonly IProgress<ThreadedWaitDialogProgressData> _progress;

        public DialogProgress(IProgress<ThreadedWaitDialogProgressData> progress)
        {
            _progress = progress;
        }

        public void Report(ProgressDialogData value)
        {
            _progress.Report(new ThreadedWaitDialogProgressData(value.WaitMessage, value.ProgressText, null, value.IsCancelable, value.CurrentStep, value.TotalSteps));
        }
#endif
    }
}
