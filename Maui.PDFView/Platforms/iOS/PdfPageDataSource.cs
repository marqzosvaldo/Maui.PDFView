using System;
using Foundation;
using PdfKit;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPageDataSource : UIPageViewControllerDataSource
    {
        private readonly PdfDocument _document;
        private readonly PdfPageRenderer _renderer;
        private readonly PageAppearance? _appearance;
        private readonly float _maxZoom;
        private readonly Action<bool>? _onZoomStateChanged;

        public PdfPageDataSource(
            PdfDocument document,
            PdfPageRenderer renderer,
            PageAppearance? appearance,
            float maxZoom,
            Action<bool>? onZoomStateChanged = null)
        {
            _document = document;
            _renderer = renderer;
            _appearance = appearance;
            _maxZoom = maxZoom;
            _onZoomStateChanged = onZoomStateChanged;
        }

        public PdfPageViewController? CreateViewController(uint pageIndex)
        {
            if (pageIndex >= _document.PageCount)
                return null;

            return new PdfPageViewController(
                pageIndex,
                _document,
                _renderer,
                _appearance,
                _maxZoom)
            {
                OnZoomStateChanged = _onZoomStateChanged
            };
        }

        public override UIViewController GetPreviousViewController(
            UIPageViewController pageViewController,
            UIViewController referenceViewController)
        {
            if (pageViewController.SpineLocation == UIPageViewControllerSpineLocation.Mid)
            {
                if (referenceViewController is PdfBlankPageViewController)
                {
                    if (_document.PageCount > 0)
                    {
                        return CreateViewController((uint)(_document.PageCount - 1))!;
                    }
                    return null!;
                }

                if (referenceViewController is not PdfPageViewController currentController)
                    return null!;

                if (currentController.PageIndex == 0)
                    return null!;

                return CreateViewController(currentController.PageIndex - 1)!;
            }
            else
            {
                // SpineLocation.Min (Portrait)
                var isDark = _appearance?.IsDarkMode == true;
                if (pageViewController.DoubleSided)
                {
                    if (referenceViewController is PdfPageViewController currentController)
                    {
                        if (currentController.PageIndex == 0)
                            return null!;

                        return new PdfBlankPageViewController(isDark, currentController.PageIndex - 1);
                    }
                    else if (referenceViewController is PdfBlankPageViewController blankController)
                    {
                        return CreateViewController(blankController.PageIndex)!;
                    }

                    return null!;
                }
                else
                {
                    if (referenceViewController is not PdfPageViewController currentController)
                        return null!;

                    if (currentController.PageIndex == 0)
                        return null!;

                    return CreateViewController(currentController.PageIndex - 1)!;
                }
            }
        }

        public override UIViewController GetNextViewController(
            UIPageViewController pageViewController,
            UIViewController referenceViewController)
        {
            if (pageViewController.SpineLocation == UIPageViewControllerSpineLocation.Mid)
            {
                if (referenceViewController is PdfBlankPageViewController)
                    return null!;

                if (referenceViewController is not PdfPageViewController currentController)
                    return null!;

                if (currentController.PageIndex + 1 >= _document.PageCount)
                {
                    if (currentController.PageIndex % 2 == 0)
                    {
                        return new PdfBlankPageViewController(_appearance?.IsDarkMode == true);
                    }
                    return null!;
                }

                return CreateViewController(currentController.PageIndex + 1)!;
            }
            else
            {
                // SpineLocation.Min (Portrait)
                var isDark = _appearance?.IsDarkMode == true;
                if (pageViewController.DoubleSided)
                {
                    if (referenceViewController is PdfPageViewController currentController)
                    {
                        if (currentController.PageIndex + 1 >= _document.PageCount)
                            return null!;

                        return new PdfBlankPageViewController(isDark, currentController.PageIndex);
                    }
                    else if (referenceViewController is PdfBlankPageViewController blankController)
                    {
                        if (blankController.PageIndex + 1 >= _document.PageCount)
                            return null!;

                        return CreateViewController(blankController.PageIndex + 1)!;
                    }

                    return null!;
                }
                else
                {
                    if (referenceViewController is not PdfPageViewController currentController)
                        return null!;

                    if (currentController.PageIndex + 1 >= _document.PageCount)
                        return null!;

                    return CreateViewController(currentController.PageIndex + 1)!;
                }
            }
        }
    }
}
