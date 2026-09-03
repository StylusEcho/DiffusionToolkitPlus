using System.Threading.Tasks;
using Diffusion.Toolkit.Classes;
using Diffusion.Toolkit.Models;
using Diffusion.Toolkit.Services;

namespace Diffusion.Toolkit
{
    public partial class MainWindow
    {
        /// <summary>
        /// Sort order whose page numbers mean nothing, because SQLite re-evaluates RANDOM() on
        /// every query - page 3 is a different set each time it is asked for.
        /// </summary>
        private const string RandomSort = "Random";

        private void InitReview()
        {
            _model.ToggleReviewCommand = new AsyncCommand<object>(async (o) => await ToggleReview());
            _model.DiscardReviewCommand = new AsyncCommand<object>(async (o) => await DiscardReview());

            // A review left running when the app was closed comes back paused rather than active,
            // so the user is never dropped straight into a locked view they did not ask for
            _model.HasReviewSession = ServiceLocator.ExtendedSettings.ReviewSession != null;
            _model.IsReviewing = false;
        }

        /// <summary>
        /// The single button: start a review, leave the one running, or pick up where the last
        /// one was left.
        /// </summary>
        private async Task ToggleReview()
        {
            if (_model.IsReviewing)
            {
                ExitReview();
                return;
            }

            if (ServiceLocator.ExtendedSettings.ReviewSession != null)
            {
                ResumeReview();
                return;
            }

            await StartReview();
        }

        private async Task StartReview()
        {
            var title = GetLocalizedText("Review.Caption");

            if (string.Equals(_search.Model.SortBy, RandomSort))
            {
                await _messagePopupManager.Show(GetLocalizedText("Review.RandomSort.Message"), title, PopupButtons.OK);
                return;
            }

            var session = _search.CaptureReview();

            ServiceLocator.ExtendedSettings.ReviewSession = session;

            _model.HasReviewSession = true;
            _model.IsReviewing = true;

            // Re-run the search so the view reflects the review's own query - the hide settings
            // are put aside for the duration, which changes what is on screen
            _search.ApplyReview(session, true);

            ServiceLocator.ToastService.Toast(GetLocalizedText("Review.Started.Message"), title);
        }

        /// <summary>
        /// Leaves the review running in the background: the lock comes off and the user's own hide
        /// settings come back, but where they had got to is kept for next time.
        /// </summary>
        private void ExitReview()
        {
            var session = ServiceLocator.ExtendedSettings.ReviewSession;

            _model.IsReviewing = false;

            if (session == null) return;

            _search.SaveReviewProgress();

            _model.HideNSFW = session.SuspendedHideNSFW;
            _model.HideDeleted = session.SuspendedHideDeleted;

            _model.HasReviewSession = true;

            ServiceLocator.ToastService.Toast(GetLocalizedText("Review.Paused.Message"), GetLocalizedText("Review.Caption"));
        }

        private void ResumeReview()
        {
            var session = ServiceLocator.ExtendedSettings.ReviewSession;

            if (session == null) return;

            _model.IsReviewing = true;

            _search.ApplyReview(session, true);

            ServiceLocator.ToastService.Toast(
                GetLocalizedText("Review.Resumed.Message").Replace("{page}", $"{session.Page}"),
                GetLocalizedText("Review.Caption"));
        }

        private async Task DiscardReview()
        {
            var session = ServiceLocator.ExtendedSettings.ReviewSession;

            if (session == null) return;

            var title = GetLocalizedText("Review.Caption");

            var result = await _messagePopupManager.Show(GetLocalizedText("Review.Discard.Message"), title, PopupButtons.YesNo);

            if (result != PopupResult.Yes) return;

            if (_model.IsReviewing)
            {
                ExitReview();
            }
            else
            {
                _model.HideNSFW = session.SuspendedHideNSFW;
                _model.HideDeleted = session.SuspendedHideDeleted;
            }

            ServiceLocator.ExtendedSettings.ReviewSession = null;

            _model.HasReviewSession = false;
        }
    }
}
