using System;
using CoreGraphics;
using Foundation;
using PdfKit;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPageViewController : UIViewController
    {
        private readonly PdfDocument _document;
        private readonly PdfPageRenderer _renderer;
        private readonly PageAppearance? _appearance;
        private readonly float _maxZoom;
        private UIScrollView? _scrollView;
        private UIImageView? _imageView;
        private CGSize _renderedSize = CGSize.Empty;

        public uint PageIndex { get; }
        public Action<bool>? OnZoomStateChanged { get; set; }

        public PdfPageViewController(
            uint pageIndex,
            PdfDocument document,
            PdfPageRenderer renderer,
            PageAppearance? appearance,
            float maxZoom)
        {
            PageIndex = pageIndex;
            _document = document;
            _renderer = renderer;
            _appearance = appearance;
            _maxZoom = Math.Max(1.0f, maxZoom);
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = _appearance?.IsDarkMode == true
                ? UIColor.FromRGB(31, 31, 34)
                : UIColor.White;

            _imageView = new UIImageView(View.Bounds)
            {
                ContentMode = UIViewContentMode.ScaleAspectFit,
                AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
                ClipsToBounds = true,
                UserInteractionEnabled = true
            };

            if (_maxZoom > 1.0f)
            {
                _scrollView = new UIScrollView(View.Bounds)
                {
                    AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
                    MaximumZoomScale = _maxZoom,
                    MinimumZoomScale = 1.0f,
                    BouncesZoom = true,
                    ShowsHorizontalScrollIndicator = false,
                    ShowsVerticalScrollIndicator = false
                };

                _scrollView.ViewForZoomingInScrollView = _ => _imageView;
                _scrollView.DidZoom += OnScrollViewDidZoom;

                // Double tap gesture for quick zoom in / out
                var doubleTap = new UITapGestureRecognizer(HandleDoubleTap)
                {
                    NumberOfTapsRequired = 2
                };
                _scrollView.AddGestureRecognizer(doubleTap);

                _scrollView.AddSubview(_imageView);
                View.AddSubview(_scrollView);
            }
            else
            {
                View.AddSubview(_imageView);
            }
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();

            var currentSize = View!.Bounds.Size;
            if (currentSize.Width > 0 && currentSize.Height > 0 &&
                (_imageView?.Image == null || Math.Abs(_renderedSize.Width - currentSize.Width) > 1 || Math.Abs(_renderedSize.Height - currentSize.Height) > 1))
            {
                _renderedSize = currentSize;
                UpdatePageImage(currentSize);
            }
        }

        private void UpdatePageImage(CGSize targetSize)
        {
            if (_imageView == null)
                return;

            var image = _renderer.RenderPage(_document, PageIndex, targetSize, _appearance);
            _imageView.Image = image;
        }

        private void OnScrollViewDidZoom(object? sender, EventArgs e)
        {
            if (_scrollView == null)
                return;

            var isZoomed = _scrollView.ZoomScale > 1.05f;
            OnZoomStateChanged?.Invoke(isZoomed);
        }

        private void HandleDoubleTap(UITapGestureRecognizer recognizer)
        {
            if (_scrollView == null)
                return;

            if (_scrollView.ZoomScale > 1.0f)
            {
                _scrollView.SetZoomScale(1.0f, animated: true);
            }
            else
            {
                var touchPoint = recognizer.LocationInView(_imageView);
                var newScale = Math.Min(_maxZoom, 2.5f);
                var zoomRect = GetRectForScale(newScale, touchPoint);
                _scrollView.ZoomToRect(zoomRect, animated: true);
            }
        }

        private CGRect GetRectForScale(nfloat scale, CGPoint center)
        {
            if (_scrollView == null)
                return CGRect.Empty;

            var size = new CGSize(
                _scrollView.Frame.Size.Width / scale,
                _scrollView.Frame.Size.Height / scale);

            return new CGRect(
                center.X - (size.Width / 2.0),
                center.Y - (size.Height / 2.0),
                size.Width,
                size.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_scrollView != null)
                {
                    _scrollView.DidZoom -= OnScrollViewDidZoom;
                    _scrollView.ViewForZoomingInScrollView = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
