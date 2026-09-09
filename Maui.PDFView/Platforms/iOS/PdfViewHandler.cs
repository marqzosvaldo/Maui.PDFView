using System;
using CoreGraphics;
using Foundation;
using Maui.PDFView.Events;
using Maui.PDFView.Helpers;
using Microsoft.Maui.Handlers;
using PdfKit;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfViewHandler : ViewHandler<IPdfView, PdfPlatformContainerView>
    {
        public static readonly PropertyMapper<IPdfView, PdfViewHandler> PropertyMapper = new(ViewMapper)
        {
            [nameof(IPdfView.Uri)] = MapUri,
            [nameof(IPdfView.IsHorizontal)] = MapIsHorizontal,
            [nameof(IPdfView.MaxZoom)] = MapMaxZoom,
            [nameof(IPdfView.PageAppearance)] = MapPageAppearance,
            [nameof(IPdfView.PageIndex)] = MapPageIndex,
            [nameof(IPdfView.TransitionMode)] = MapTransitionMode,
            [nameof(IPdfView.DoubleSided)] = MapDoubleSided,
        };

        private string? _fileName;
        private PageAppearance _appearance = new();
        private readonly DesiredSizeHelper _sizeHelper = new();
        private bool _isScrolling;

        // PdfKit Continuous Scroll mode components
        private PdfKit.PdfView? _pdfKitView;
        private NSObject? _pdfKitNotificationToken;

        // UIPageViewController PageCurl mode components
        private UIPageViewController? _pageViewController;
        private PdfPageDataSource? _pageDataSource;
        private PdfPageDelegate? _pageDelegate;
        private readonly PdfPageRenderer _pageRenderer = new();
        private PdfDocument? _pdfDocument;

        public PdfViewHandler() : base(PropertyMapper, null)
        {
        }

        static void MapUri(PdfViewHandler handler, IPdfView pdfView)
        {
            handler._fileName = pdfView.Uri;
            handler.LoadDocument();
        }

        static void MapIsHorizontal(PdfViewHandler handler, IPdfView pdfView)
        {
            if (handler._pdfKitView != null)
            {
                handler._pdfKitView.DisplayDirection = pdfView.IsHorizontal
                    ? PdfDisplayDirection.Horizontal
                    : PdfDisplayDirection.Vertical;
            }

            if (pdfView.TransitionMode == PdfTransitionMode.PageCurl)
            {
                handler.SetupPageCurlMode();
            }
        }

        static void MapMaxZoom(PdfViewHandler handler, IPdfView pdfView)
        {
            if (pdfView.TransitionMode == PdfTransitionMode.ContinuousScroll)
            {
                handler.RenderContinuousPages();
            }
            else
            {
                handler.SetupPageCurlMode();
            }
        }

        static void MapPageAppearance(PdfViewHandler handler, IPdfView pdfView)
        {
            handler._appearance = pdfView.PageAppearance ?? new PageAppearance();

            if (handler._pdfKitView != null)
            {
                SetPdfKitAppearance(handler._pdfKitView, handler._appearance);
            }

            if (pdfView.TransitionMode == PdfTransitionMode.PageCurl)
            {
                handler._pageRenderer.ClearCache();
                handler.GotoPageCurl(pdfView.PageIndex, animated: false);
            }
        }

        static void MapPageIndex(PdfViewHandler handler, IPdfView pdfView)
        {
            handler.GotoPage(pdfView.PageIndex);
        }

        static void MapTransitionMode(PdfViewHandler handler, IPdfView pdfView)
        {
            handler.ApplyTransitionMode();
        }

        static void MapDoubleSided(PdfViewHandler handler, IPdfView pdfView)
        {
            if (handler._pageViewController != null)
            {
                handler._pageViewController.DoubleSided = pdfView.DoubleSided;
            }
        }

        protected override PdfPlatformContainerView CreatePlatformView()
        {
            return new PdfPlatformContainerView();
        }

        public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            if (_sizeHelper.UpdateSize(widthConstraint, heightConstraint))
            {
                if (VirtualView.TransitionMode == PdfTransitionMode.ContinuousScroll)
                {
                    RenderContinuousPages();
                }
            }

            return base.GetDesiredSize(widthConstraint, heightConstraint);
        }

        protected override void DisconnectHandler(PdfPlatformContainerView platformView)
        {
            CleanUpPdfKitView();
            CleanUpPageViewController();
            _pageRenderer.Dispose();
            platformView.DetachCurrent();
            _pdfDocument = null;
            base.DisconnectHandler(platformView);
        }

        private void LoadDocument()
        {
            _pageRenderer.ClearCache();

            if (string.IsNullOrEmpty(_fileName))
            {
                _pdfDocument = null;
                CleanUpPdfKitView();
                CleanUpPageViewController();
                PlatformView.DetachCurrent();
                return;
            }

            var data = NSData.FromFile(_fileName);
            if (data == null)
            {
                _pdfDocument = null;
                return;
            }

            _pdfDocument = new PdfDocument(data);
            if (_pdfDocument != null)
            {
                CropPages(_pdfDocument, _appearance.Crop);
            }

            ApplyTransitionMode();
        }

        private void ApplyTransitionMode()
        {
            if (VirtualView == null || _pdfDocument == null)
                return;

            if (VirtualView.TransitionMode == PdfTransitionMode.PageCurl)
            {
                CleanUpPdfKitView();
                SetupPageCurlMode();
            }
            else
            {
                CleanUpPageViewController();
                SetupContinuousScrollMode();
            }
        }

        #region Continuous Scroll Mode (PdfKit.PdfView)

        private void SetupContinuousScrollMode()
        {
            if (_pdfKitView == null)
            {
                _pdfKitView = new PdfKit.PdfView();
                _pdfKitNotificationToken = NSNotificationCenter.DefaultCenter.AddObserver(
                    PdfKit.PdfView.PageChangedNotification,
                    PageChangedNotificationHandler,
                    _pdfKitView);
            }

            SetPdfKitAppearance(_pdfKitView, _appearance);
            PlatformView.SetContentView(_pdfKitView);
            RenderContinuousPages();

            if (VirtualView != null)
            {
                GotoPdfKitPage(VirtualView.PageIndex);
            }
        }

        private void RenderContinuousPages()
        {
            if (_pdfKitView == null || _pdfDocument == null)
                return;

            _pdfKitView.Document = _pdfDocument;
            _pdfKitView.AutosizesSubviews = true;
            _pdfKitView.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            _pdfKitView.DisplayMode = PdfDisplayMode.SinglePageContinuous;
            _pdfKitView.DisplaysPageBreaks = true;
            _pdfKitView.DisplayDirection = VirtualView?.IsHorizontal == true
                ? PdfDisplayDirection.Horizontal
                : PdfDisplayDirection.Vertical;

            _pdfKitView.MaxScaleFactor = VirtualView?.MaxZoom ?? 4f;
            _pdfKitView.MinScaleFactor = (nfloat)(UIScreen.MainScreen.Bounds.Height * 0.00075);
            _pdfKitView.AutoScales = true;
        }

        private static void SetPdfKitAppearance(PdfKit.PdfView pdfView, PageAppearance appearance)
        {
            if (OperatingSystem.IsIOSVersionAtLeast(12, 0))
            {
                pdfView.PageShadowsEnabled = appearance.ShadowEnabled;
            }

            pdfView.PageBreakMargins = new UIEdgeInsets(
                (nfloat)appearance.Margin.Top,
                (nfloat)appearance.Margin.Left,
                (nfloat)appearance.Margin.Bottom,
                (nfloat)appearance.Margin.Right);
        }

        private void GotoPdfKitPage(uint pageIndex)
        {
            if (_isScrolling || _pdfKitView?.Document == null)
                return;

            if (pageIndex >= _pdfKitView.Document.PageCount)
                return;

            var newPage = _pdfKitView.Document.GetPage((nint)pageIndex);
            if (newPage != null)
            {
                _pdfKitView.GoToPage(newPage);
            }
        }

        private void PageChangedNotificationHandler(NSNotification notification)
        {
            var platformPdfView = notification.Object as PdfKit.PdfView;
            var virtualView = VirtualView;

            if (platformPdfView?.Document == null || virtualView == null)
                return;

            var currentPage = platformPdfView.CurrentPage;
            if (currentPage == null)
                return;

            var newPageIndex = (uint)platformPdfView.Document.GetPageIndex(currentPage);
            if (virtualView.PageIndex != newPageIndex)
            {
                _isScrolling = true;
                virtualView.PageIndex = newPageIndex;
                _isScrolling = false;
            }

            if (virtualView.PageChangedCommand?.CanExecute(null) == true)
            {
                virtualView.PageChangedCommand.Execute(new PageChangedEventArgs((int)newPageIndex + 1, (int)platformPdfView.Document.PageCount));
            }
        }

        private void CleanUpPdfKitView()
        {
            if (_pdfKitNotificationToken != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_pdfKitNotificationToken);
                _pdfKitNotificationToken = null;
            }

            if (_pdfKitView != null)
            {
                _pdfKitView.Document = null;
                _pdfKitView.RemoveFromSuperview();
                _pdfKitView.Dispose();
                _pdfKitView = null;
            }
        }

        #endregion

        #region Page Curl Mode (UIPageViewController)

        private void SetupPageCurlMode()
        {
            if (_pdfDocument == null)
                return;

            CleanUpPageViewController();

            var orientation = VirtualView?.IsHorizontal == false
                ? UIPageViewControllerNavigationOrientation.Vertical
                : UIPageViewControllerNavigationOrientation.Horizontal;

            var spineLocation = UIPageViewControllerSpineLocation.Min;

            _pageViewController = new UIPageViewController(
                UIPageViewControllerTransitionStyle.PageCurl,
                orientation,
                spineLocation)
            {
                DoubleSided = VirtualView?.DoubleSided ?? false
            };

            _pageDataSource = new PdfPageDataSource(
                _pdfDocument,
                _pageRenderer,
                _appearance,
                VirtualView?.MaxZoom ?? 1.0f,
                OnZoomStateChanged);

            _pageDelegate = new PdfPageDelegate(OnPageCurlFinishedAnimating);

            _pageViewController.DataSource = _pageDataSource;
            _pageViewController.Delegate = _pageDelegate;

            PlatformView.SetContentView(_pageViewController.View!, _pageViewController);

            var initialIndex = VirtualView?.PageIndex ?? 0;
            GotoPageCurl(initialIndex, animated: false);
        }

        private void OnZoomStateChanged(bool isZoomed)
        {
            if (_pageViewController?.GestureRecognizers == null)
                return;

            // When zoomed into page details, disable curl gestures to allow free panning
            foreach (var gesture in _pageViewController.GestureRecognizers)
            {
                gesture.Enabled = !isZoomed;
            }
        }

        private void OnPageCurlFinishedAnimating(uint newPageIndex)
        {
            var virtualView = VirtualView;
            if (virtualView == null || _pdfDocument == null)
                return;

            if (virtualView.PageIndex != newPageIndex)
            {
                _isScrolling = true;
                virtualView.PageIndex = newPageIndex;
                _isScrolling = false;
            }

            if (virtualView.PageChangedCommand?.CanExecute(null) == true)
            {
                virtualView.PageChangedCommand.Execute(
                    new PageChangedEventArgs((int)newPageIndex + 1, (int)_pdfDocument.PageCount));
            }
        }

        private void GotoPageCurl(uint pageIndex, bool animated = true)
        {
            if (_isScrolling || _pageViewController == null || _pdfDocument == null || _pageDataSource == null)
                return;

            if (pageIndex >= _pdfDocument.PageCount)
                return;

            var currentControllers = _pageViewController.ViewControllers;
            uint currentIndex = 0;
            if (currentControllers != null && currentControllers.Length > 0 &&
                currentControllers[0] is PdfPageViewController currentVC)
            {
                currentIndex = currentVC.PageIndex;
                if (currentIndex == pageIndex && animated)
                    return;
            }

            var targetController = _pageDataSource.CreateViewController(pageIndex);
            if (targetController == null)
                return;

            var direction = pageIndex >= currentIndex
                ? UIPageViewControllerNavigationDirection.Forward
                : UIPageViewControllerNavigationDirection.Reverse;

            _pageViewController.SetViewControllers(
                new UIViewController[] { targetController },
                direction,
                animated,
                null);
        }

        private void CleanUpPageViewController()
        {
            if (_pageViewController != null)
            {
                _pageViewController.DataSource = null!;
                _pageViewController.Delegate = null!;
                _pageViewController.View?.RemoveFromSuperview();
                _pageViewController.Dispose();
                _pageViewController = null;
            }

            _pageDataSource = null;
            _pageDelegate = null;
        }

        #endregion

        private void GotoPage(uint pageIndex)
        {
            if (VirtualView?.TransitionMode == PdfTransitionMode.PageCurl)
            {
                GotoPageCurl(pageIndex, animated: true);
            }
            else
            {
                GotoPdfKitPage(pageIndex);
            }
        }

        private static void CropPages(PdfKit.PdfDocument pdfdoc, Thickness cropBounds)
        {
            if (cropBounds.IsEmpty)
                return;

            for (var i = 0; i < pdfdoc.PageCount; ++i)
            {
                var page = pdfdoc.GetPage(i);
                if (page == null)
                    continue;

                var boundW = cropBounds.Left + cropBounds.Right;
                var boundH = cropBounds.Top + cropBounds.Bottom;

                var boxType = PdfKit.PdfDisplayBox.Crop;
                var oldBounds = page.GetBoundsForBox(boxType);
                page.SetBoundsForBox(new CGRect(cropBounds.Left, cropBounds.Top, oldBounds.Width - boundW, oldBounds.Height - boundH), boxType);
            }
        }
    }
}
