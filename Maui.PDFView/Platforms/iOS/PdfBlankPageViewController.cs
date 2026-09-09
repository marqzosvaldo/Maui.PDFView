using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfBlankPageViewController : UIViewController
    {
        public bool IsDarkMode { get; set; }

        public PdfBlankPageViewController(bool isDarkMode = false)
        {
            IsDarkMode = isDarkMode;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = IsDarkMode
                ? UIColor.FromRGB(31, 31, 34)
                : UIColor.White;
        }
    }
}
