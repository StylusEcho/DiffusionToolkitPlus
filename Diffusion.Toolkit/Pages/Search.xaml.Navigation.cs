using System;
using System.Linq;

namespace Diffusion.Toolkit.Pages
{
    public partial class Search
    {
        /// <summary>
        /// The index of the cursor when the navigation started
        /// </summary>
        private int _startIndex = -1;

        public void StartNavigateCursor()
        {
            if (isPaging) return;
            if (_startIndex == -1 && _model.SelectedImageEntry != null)
            {
                _startIndex = _model.Images.IndexOf(_model.SelectedImageEntry);
            }
        }

        public void EndNavigateCursor()
        {
            if (isPaging) return;
            _startIndex = -1;
        }


        public void Advance()
        {
            StartNavigateCursor();
            NavigateCursorNext();
        }

        private bool isPaging = false;

        public void NavigateCursorNext()
        {
            if (isPaging) return;

            if (_model.Images == null) return;

            int currentIndex = 0;

            var lastIndex = _model.Images.Count - 1;

            var empty = _model.Images.FirstOrDefault(d => d.IsEmpty);

            if (empty != null)
            {
                lastIndex = _model.Images.IndexOf(empty) - 1;
            }

            if (_model.SelectedImageEntry != null)
            {
                currentIndex = _model.Images.IndexOf(_model.SelectedImageEntry);
            }

            if (currentIndex < lastIndex)
            {
                ThumbnailListView.ShowItem(currentIndex + 1);
                _model.SelectedImageEntry = _model.Images[currentIndex + 1];
                ThumbnailListView.ThumbnailListView.SelectedItem = _model.SelectedImageEntry;
            }
            else
            {
                if (_startIndex == lastIndex)
                {
                    isPaging = true;

                    var paged = ThumbnailListView.GoNextPage(() =>
                    {
                        // Released whatever happens in here. This also runs after a reload that
                        // failed, when the page may be empty, and anything throwing before the
                        // release used to leave navigation switched off for the rest of the session
                        try
                        {
                            _startIndex = 0;

                            if (_model.Images is { Count: > 0 } && !_model.Images[0].IsEmpty)
                            {
                                _model.SelectedImageEntry = _model.Images[0];
                                ThumbnailListView.ThumbnailListView.SelectedItem = _model.SelectedImageEntry;
                            }

                            NavigationCompleted?.Invoke(this, new EventArgs());
                        }
                        finally
                        {
                            isPaging = false;
                        }
                    });

                    if (!paged)
                    {
                        isPaging = false;
                    }

                }
            }

        }

        public void NavigateCursorPrevious()
        {
            if (isPaging) return;
            if (_model.Images == null) return;
            int currentIndex = 0;
            if (_model.SelectedImageEntry != null)
            {
                currentIndex = _model.Images.IndexOf(_model.SelectedImageEntry);
            }

            if (currentIndex > 0)
            {
                ThumbnailListView.ShowItem(currentIndex - 1);
                _model.SelectedImageEntry = _model.Images[currentIndex - 1];
                ThumbnailListView.ThumbnailListView.SelectedItem = _model.SelectedImageEntry;
            }
            else
            {
                if (_startIndex == 0)
                {
                    isPaging = true;
                    var paged = ThumbnailListView.GoPrevPage(() =>
                    {
                        // Released whatever happens in here - see the note in NavigateCursorNext
                        try
                        {
                            var images = _model.Images;

                            if (images is { Count: > 0 })
                            {
                                var empty = images.FirstOrDefault(d => d.IsEmpty);
                                var lastIndex = images.Count - 1;
                                if (empty != null)
                                {
                                    lastIndex = images.IndexOf(empty) - 1;
                                }

                                // A page that is all padding has no last image to land on
                                if (lastIndex >= 0)
                                {
                                    _startIndex = lastIndex;

                                    _model.SelectedImageEntry = images[lastIndex];
                                    ThumbnailListView.ThumbnailListView.SelectedItem = _model.SelectedImageEntry;
                                }
                            }

                            NavigationCompleted?.Invoke(this, new EventArgs());
                        }
                        finally
                        {
                            isPaging = false;
                        }
                    }, true);

                    if (!paged)
                    {
                        isPaging = false;
                    }

                }
            }

        }

    }
}
