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
            [nameof(IPdfView.IsDualPage)] = MapIsDualPage,
            [nameof(IPdfView.IsDarkMode)] = MapIsDarkMode,
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
        private UIPageViewControllerSpineLocation _currentSpineLocation = UIPageViewControllerSpineLocation.Min;

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
                    ? PdfKit.PdfDisplayDirection.Horizontal
                    : PdfKit.PdfDisplayDirection.Vertical;
            }

            if (pdfView.TransitionMode == PdfTransitionMode.PageCurl)
            {
                handler.SetupPageCurlMode();
            }
        }

        static void MapMaxZoom(PdfViewHandler handler, IPdfView pdfView)
        {
            if (handler._pdfKitView != null)
            {
                handler._pdfKitView.MaxScaleFactor = pdfView.MaxZoom;
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

        static void MapIsDualPage(PdfViewHandler handler, IPdfView pdfView)
        {
            // OneWayToSource from platform to VirtualView
        }

        static void MapIsDarkMode(PdfViewHandler handler, IPdfView pdfView)
        {
            handler._appearance.IsDarkMode = pdfView.IsDarkMode;
            var themeBg = pdfView.IsDarkMode
                ? UIColor.FromRGB(31, 31, 34)
                : UIColor.White;

            if (handler._pageViewController?.View != null)
            {
                handler._pageViewController.View.BackgroundColor = themeBg;
            }

            if (handler.PlatformView != null)
            {
                handler.PlatformView.BackgroundColor = themeBg;
            }

            if (pdfView.TransitionMode == PdfTransitionMode.PageCurl)
            {
                if (handler._pageViewController != null && handler.PlatformView != null)
                {
                    var isLandscape = handler.DetermineIfLandscape(handler.PlatformView.Bounds.Width, handler.PlatformView.Bounds.Height);
                    handler._pageViewController.DoubleSided = isLandscape || (handler.VirtualView?.DoubleSided ?? false);
                }

                handler._pageRenderer.ClearCache();
                handler.GotoPageCurl(pdfView.PageIndex, animated: false);
            }
        }

        protected override PdfPlatformContainerView CreatePlatformView()
        {
            return new PdfPlatformContainerView();
        }

        protected override void ConnectHandler(PdfPlatformContainerView platformView)
        {
            base.ConnectHandler(platformView);
            platformView.OnBoundsChanged = OnPlatformViewBoundsChanged;
        }

        private void OnPlatformViewBoundsChanged(CGSize newSize)
        {
            if (VirtualView?.TransitionMode == PdfTransitionMode.PageCurl && newSize.Width > 0 && newSize.Height > 0)
            {
                CheckAndReconfigureSpineLocation(newSize.Width, newSize.Height);
            }
        }

        public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            if (_sizeHelper.UpdateSize(widthConstraint, heightConstraint))
            {
                if (VirtualView?.TransitionMode == PdfTransitionMode.ContinuousScroll)
                {
                    RenderContinuousPages();
                }
            }

            return base.GetDesiredSize(widthConstraint, heightConstraint);
        }

        public override void PlatformArrange(Microsoft.Maui.Graphics.Rect frame)
        {
            base.PlatformArrange(frame);

            if (VirtualView?.TransitionMode == PdfTransitionMode.PageCurl && frame.Width > 0 && frame.Height > 0)
            {
                CheckAndReconfigureSpineLocation(frame.Width, frame.Height);
            }
        }

        protected override void DisconnectHandler(PdfPlatformContainerView platformView)
        {
            platformView.OnBoundsChanged = null;
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
                NotifyPageChanged(VirtualView.PageIndex);
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

        private bool DetermineIfLandscape(double width = 0, double height = 0)
        {
            var isPad = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad;
            if (!isPad)
                return false;

            if (width > 0 && height > 0 && !double.IsInfinity(width) && !double.IsInfinity(height) && Math.Abs(width - height) > 20)
            {
                return width > height;
            }

            if (PlatformView != null && PlatformView.Bounds.Width > 0 && PlatformView.Bounds.Height > 0 && Math.Abs(PlatformView.Bounds.Width - PlatformView.Bounds.Height) > 20)
            {
                return PlatformView.Bounds.Width > PlatformView.Bounds.Height;
            }

            if (VirtualView != null && VirtualView.Width > 0 && VirtualView.Height > 0 && Math.Abs(VirtualView.Width - VirtualView.Height) > 20)
            {
                return VirtualView.Width > VirtualView.Height;
            }

            var windowScene = UIApplication.SharedApplication.ConnectedScenes.ToArray()
                .OfType<UIWindowScene>()
                .FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive)
                ?? UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>().FirstOrDefault();

            if (windowScene != null && windowScene.InterfaceOrientation != UIInterfaceOrientation.Unknown)
            {
                return windowScene.InterfaceOrientation == UIInterfaceOrientation.LandscapeLeft ||
                       windowScene.InterfaceOrientation == UIInterfaceOrientation.LandscapeRight;
            }

            return UIScreen.MainScreen.Bounds.Width > UIScreen.MainScreen.Bounds.Height;
        }

        private void CheckAndReconfigureSpineLocation(double width = 0, double height = 0)
        {
            if (_pdfDocument == null || VirtualView?.TransitionMode != PdfTransitionMode.PageCurl)
                return;

            var targetSpine = DetermineIfLandscape(width, height)
                ? UIPageViewControllerSpineLocation.Mid
                : UIPageViewControllerSpineLocation.Min;

            if (_pageViewController == null || _currentSpineLocation != targetSpine)
            {
                SetupPageCurlMode(width, height);
            }
        }

        private void SetupPageCurlMode(double width = 0, double height = 0)
        {
            if (_pdfDocument == null)
                return;

            CleanUpPageViewController();

            var orientation = VirtualView?.IsHorizontal == false
                ? UIPageViewControllerNavigationOrientation.Vertical
                : UIPageViewControllerNavigationOrientation.Horizontal;

            var isLandscape = DetermineIfLandscape(width, height);
            var isDark = _appearance?.IsDarkMode == true;
            var spineLocation = isLandscape
                ? UIPageViewControllerSpineLocation.Mid
                : UIPageViewControllerSpineLocation.Min;

            _currentSpineLocation = spineLocation;

            _pageViewController = new UIPageViewController(
                UIPageViewControllerTransitionStyle.PageCurl,
                orientation,
                spineLocation)
            {
                DoubleSided = isLandscape || (VirtualView?.DoubleSided ?? false)
            };

            var themeBg = _appearance?.IsDarkMode == true
                ? UIColor.FromRGB(31, 31, 34)
                : UIColor.White;

            if (_pageViewController.View != null)
            {
                _pageViewController.View.BackgroundColor = themeBg;
            }
            PlatformView.BackgroundColor = themeBg;

            _pageDataSource = new PdfPageDataSource(
                _pdfDocument,
                _pageRenderer,
                _appearance,
                VirtualView?.MaxZoom ?? 1.0f,
                OnZoomStateChanged);

            _pageDelegate = new PdfPageDelegate(
                _pdfDocument,
                _pageDataSource,
                _appearance,
                OnPageCurlFinishedAnimating,
                OnSpineChangedFromDelegate,
                CreateBlankPageViewController);

            _pageViewController.DataSource = _pageDataSource;
            _pageViewController.Delegate = _pageDelegate;

            PlatformView.SetContentView(_pageViewController.View!, _pageViewController);

            UpdateVirtualViewDualPage(isLandscape);

            var initialIndex = VirtualView?.PageIndex ?? 0;
            GotoPageCurl(initialIndex, animated: false);
        }

        private void OnSpineChangedFromDelegate(bool isDualPage, uint currentPageIndex)
        {
            _currentSpineLocation = isDualPage
                ? UIPageViewControllerSpineLocation.Mid
                : UIPageViewControllerSpineLocation.Min;

            UpdateVirtualViewDualPage(isDualPage);

            var virtualView = VirtualView;
            if (virtualView != null && virtualView.PageIndex != currentPageIndex)
            {
                _isScrolling = true;
                virtualView.PageIndex = currentPageIndex;
                _isScrolling = false;

                if (virtualView.PageChangedCommand?.CanExecute(null) == true && _pdfDocument != null)
                {
                    virtualView.PageChangedCommand.Execute(
                        new PageChangedEventArgs((int)currentPageIndex + 1, (int)_pdfDocument.PageCount));
                }
            }
        }

        private void UpdateVirtualViewDualPage(bool isDualPage)
        {
            if (VirtualView != null && VirtualView.IsDualPage != isDualPage)
            {
                VirtualView.IsDualPage = isDualPage;
            }
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
            if (currentControllers != null && currentControllers.Length > 0)
            {
                if (currentControllers[0] is PdfPageViewController currentVC)
                {
                    currentIndex = currentVC.PageIndex;
                }
                else if (currentControllers[0] is PdfBlankPageViewController blankVC)
                {
                    currentIndex = blankVC.PageIndex;
                }
            }

            if (_pageViewController.SpineLocation == UIPageViewControllerSpineLocation.Mid)
            {
                uint leftPage = (pageIndex % 2 == 0) ? pageIndex : pageIndex - 1;
                uint rightPage = leftPage + 1;

                if (currentControllers != null && currentControllers.Length == 2 &&
                    currentControllers[0] is PdfPageViewController currentLeftVC)
                {
                    if (currentLeftVC.PageIndex == leftPage && animated)
                        return;
                }

                var leftController = _pageDataSource.CreateViewController(leftPage);
                if (leftController == null)
                    return;

                UIViewController rightController;
                if (rightPage < _pdfDocument.PageCount)
                {
                    rightController = (UIViewController?)_pageDataSource.CreateViewController(rightPage) ?? CreateBlankPageViewController();
                }
                else
                {
                    rightController = CreateBlankPageViewController();
                }

                var direction = leftPage >= currentIndex
                    ? UIPageViewControllerNavigationDirection.Forward
                    : UIPageViewControllerNavigationDirection.Reverse;

                _pageViewController.SetViewControllers(
                    new UIViewController[] { leftController, rightController },
                    direction,
                    animated,
                    null);

                if (VirtualView != null)
                {
                    if (VirtualView.PageIndex != leftPage)
                    {
                        _isScrolling = true;
                        VirtualView.PageIndex = leftPage;
                        _isScrolling = false;
                    }

                    NotifyPageChanged(leftPage);
                }
            }
            else
            {
                if (currentIndex == pageIndex && animated && currentControllers != null && currentControllers.Length == 1)
                    return;

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

                if (VirtualView != null)
                {
                    if (VirtualView.PageIndex != pageIndex)
                    {
                        _isScrolling = true;
                        VirtualView.PageIndex = pageIndex;
                        _isScrolling = false;
                    }

                    NotifyPageChanged(pageIndex);
                }
            }
        }

        private void NotifyPageChanged(uint pageIndex)
        {
            var virtualView = VirtualView;
            if (virtualView?.PageChangedCommand?.CanExecute(null) == true && _pdfDocument != null)
            {
                virtualView.PageChangedCommand.Execute(
                    new PageChangedEventArgs((int)pageIndex + 1, (int)_pdfDocument.PageCount));
            }
        }

        private UIViewController CreateBlankPageViewController()
        {
            return new PdfBlankPageViewController(_appearance.IsDarkMode);
        }

        private void CleanUpPageViewController()
        {
            PlatformView?.DetachCurrent();

            if (_pageViewController != null)
            {
                _pageViewController.DataSource = null!;
                _pageViewController.Delegate = null!;
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
