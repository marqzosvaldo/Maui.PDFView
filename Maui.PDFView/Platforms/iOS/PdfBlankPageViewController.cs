using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfBlankPageViewController : UIViewController
    {
        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.White;
        }
    }
}
