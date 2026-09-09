using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfBlankPageViewController : UIViewController
    {
        public bool IsDarkMode { get; set; }
        public uint PageIndex { get; set; }

        public PdfBlankPageViewController(bool isDarkMode = false, uint pageIndex = 0)
        {
            IsDarkMode = isDarkMode;
            PageIndex = pageIndex;
        }

        public override void LoadView()
        {
            View = new UIView
            {
                BackgroundColor = IsDarkMode
                    ? UIColor.FromRGB(31, 31, 34)
                    : UIColor.White
            };
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
