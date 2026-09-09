using System;
using System.Linq;
using Foundation;
using PdfKit;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPageDelegate : UIPageViewControllerDelegate
    {
        private readonly PdfDocument _document;
        private readonly PdfPageDataSource _dataSource;
        private readonly PageAppearance? _appearance;
        private readonly Action<uint> _onPageChanged;
        private readonly Action<bool, uint>? _onSpineChanged;
        private readonly Func<UIViewController>? _createBlankPage;

        public PdfPageDelegate(
            PdfDocument document,
            PdfPageDataSource dataSource,
            PageAppearance? appearance,
            Action<uint> onPageChanged,
            Action<bool, uint>? onSpineChanged = null,
            Func<UIViewController>? createBlankPage = null)
        {
            _document = document;
            _dataSource = dataSource;
            _appearance = appearance;
            _onPageChanged = onPageChanged;
            _onSpineChanged = onSpineChanged;
            _createBlankPage = createBlankPage;
        }

        public override void DidFinishAnimating(
            UIPageViewController pageViewController,
            bool finished,
            UIViewController[] previousViewControllers,
            bool completed)
        {
            if (!completed)
                return;

            var current = pageViewController.ViewControllers;
            if (current != null && current.Length > 0 && current[0] is PdfPageViewController currentController)
            {
                _onPageChanged(currentController.PageIndex);
            }
        }

        private bool DetermineIsLandscape(UIPageViewController pageViewController, UIInterfaceOrientation orientation)
        {
            var isPad = UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad;
            if (!isPad)
                return false;

            if (orientation == UIInterfaceOrientation.LandscapeLeft || orientation == UIInterfaceOrientation.LandscapeRight)
                return true;

            if (orientation == UIInterfaceOrientation.Portrait || orientation == UIInterfaceOrientation.PortraitUpsideDown)
                return false;

            var scenes = UIApplication.SharedApplication.ConnectedScenes.ToArray();
            var windowScene = pageViewController.View?.Window?.WindowScene ??
                scenes.OfType<UIWindowScene>().FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive) ??
                scenes.OfType<UIWindowScene>().FirstOrDefault();

            if (windowScene != null && windowScene.InterfaceOrientation != UIInterfaceOrientation.Unknown)
            {
                return windowScene.InterfaceOrientation == UIInterfaceOrientation.LandscapeLeft ||
                       windowScene.InterfaceOrientation == UIInterfaceOrientation.LandscapeRight;
            }

            if (pageViewController.View != null && pageViewController.View.Bounds.Width > 0 && pageViewController.View.Bounds.Height > 0)
            {
                return pageViewController.View.Bounds.Width > pageViewController.View.Bounds.Height;
            }

            return UIScreen.MainScreen.Bounds.Width > UIScreen.MainScreen.Bounds.Height;
        }

        [Export("pageViewController:spineLocationForInterfaceOrientation:")]
        public override UIPageViewControllerSpineLocation GetSpineLocation(
            UIPageViewController pageViewController,
            UIInterfaceOrientation orientation)
        {
            var isLandscape = DetermineIsLandscape(pageViewController, orientation);

            if (isLandscape)
            {
                var currentControllers = pageViewController.ViewControllers;
                uint currentPage = 0;
                if (currentControllers != null && currentControllers.Length > 0)
                {
                    if (currentControllers[0] is PdfPageViewController currentVC)
                    {
                        currentPage = currentVC.PageIndex;
                    }
                    else if (currentControllers[0] is PdfBlankPageViewController currentBlank)
                    {
                        currentPage = currentBlank.PageIndex;
                    }
                }

                uint leftPage = (currentPage % 2 == 0) ? currentPage : currentPage - 1;
                uint rightPage = leftPage + 1;

                var leftController = _dataSource.CreateViewController(leftPage) ?? _dataSource.CreateViewController(0);
                if (leftController == null)
                    return UIPageViewControllerSpineLocation.Min;

                UIViewController rightController;
                if (rightPage < _document.PageCount)
                {
                    rightController = (UIViewController?)_dataSource.CreateViewController(rightPage) ?? (_createBlankPage?.Invoke() ?? new PdfBlankPageViewController());
                }
                else
                {
                    rightController = _createBlankPage?.Invoke() ?? new PdfBlankPageViewController();
                }

                pageViewController.SetViewControllers(
                    new UIViewController[] { leftController, rightController },
                    UIPageViewControllerNavigationDirection.Forward,
                    false,
                    null);

                pageViewController.DoubleSided = true;
                _onSpineChanged?.Invoke(true, leftPage);
                return UIPageViewControllerSpineLocation.Mid;
            }
            else
            {
                var currentControllers = pageViewController.ViewControllers;
                UIViewController? currentController = null;
                uint fallbackIndex = 0;

                if (currentControllers != null && currentControllers.Length > 0)
                {
                    if (currentControllers[0] is PdfPageViewController pvc)
                    {
                        currentController = pvc;
                        fallbackIndex = pvc.PageIndex;
                    }
                    else if (currentControllers[0] is PdfBlankPageViewController bvc)
                    {
                        currentController = _dataSource.CreateViewController(bvc.PageIndex);
                        fallbackIndex = bvc.PageIndex;
                    }
                }

                currentController ??= _dataSource.CreateViewController(0);

                pageViewController.DoubleSided = false;

                if (currentController != null)
                {
                    pageViewController.SetViewControllers(
                        new UIViewController[] { currentController },
                        UIPageViewControllerNavigationDirection.Forward,
                        false,
                        null);
                }

                _onSpineChanged?.Invoke(false, fallbackIndex);
                return UIPageViewControllerSpineLocation.Min;
            }
        }
    }
}
