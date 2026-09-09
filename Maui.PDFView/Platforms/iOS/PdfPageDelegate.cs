using System;
using Foundation;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPageDelegate : UIPageViewControllerDelegate
    {
        private readonly Action<uint> _onPageChanged;

        public PdfPageDelegate(Action<uint> onPageChanged)
        {
            _onPageChanged = onPageChanged;
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
    }
}
