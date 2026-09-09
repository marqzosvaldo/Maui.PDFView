using System;
using CoreGraphics;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPlatformContainerView : UIView
    {
        private UIView? _currentContentView;
        private UIViewController? _currentChildController;
        private UIViewController? _parentViewController;

        public PdfPlatformContainerView()
        {
            ClipsToBounds = true;
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
        }

        public Action<CGSize>? OnBoundsChanged { get; set; }

        public void SetContentView(UIView contentView, UIViewController? childViewController = null)
        {
            if (_currentContentView == contentView)
                return;

            DetachCurrent();

            _currentContentView = contentView;
            _currentChildController = childViewController;

            _currentContentView.Frame = Bounds;
            _currentContentView.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            AddSubview(_currentContentView);

            AttachChildControllerIfNeeded();
        }

        public void DetachCurrent()
        {
            if (_currentChildController != null)
            {
                if (_currentChildController.ParentViewController != null)
                {
                    _currentChildController.WillMoveToParentViewController(null);
                    _currentChildController.RemoveFromParentViewController();
                }
                _currentChildController = null;
            }

            if (_currentContentView != null)
            {
                _currentContentView.RemoveFromSuperview();
                _currentContentView = null;
            }

            _parentViewController = null;
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            if (Window != null)
            {
                AttachChildControllerIfNeeded();
            }
            else
            {
                if (_currentChildController != null && _parentViewController != null)
                {
                    _currentChildController.WillMoveToParentViewController(null);
                    _currentChildController.RemoveFromParentViewController();
                    _parentViewController = null;
                }
            }
        }

        private void AttachChildControllerIfNeeded()
        {
            if (_currentChildController == null || Window == null)
                return;

            var parent = FindParentViewController();
            if (parent != null)
            {
                _parentViewController = parent;
                if (_currentChildController.ParentViewController != parent)
                {
                    parent.AddChildViewController(_currentChildController);
                    _currentChildController.DidMoveToParentViewController(parent);
                }
            }
        }

        private UIViewController? FindParentViewController()
        {
            UIResponder? responder = this;
            while (responder != null)
            {
                if (responder is UIViewController vc)
                    return vc;
                responder = responder.NextResponder;
            }
            return null;
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            if (_currentContentView != null && _currentContentView.Frame != Bounds)
            {
                _currentContentView.Frame = Bounds;
            }
            if (Bounds.Width > 0 && Bounds.Height > 0)
            {
                var size = Bounds.Size;
                BeginInvokeOnMainThread(() =>
                {
                    OnBoundsChanged?.Invoke(size);
                });
            }
        }
    }
}
