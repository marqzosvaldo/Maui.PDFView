using System;
using CoreGraphics;
using Foundation;
using PdfKit;
using UIKit;

namespace Maui.PDFView.Platforms.iOS
{
    public class PdfPageRenderer : IDisposable
    {
        private readonly NSCache _imageCache = new();
        private NSObject? _memoryWarningObserver;

        public PdfPageRenderer()
        {
            _imageCache.CountLimit = 30;
            _memoryWarningObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.DidReceiveMemoryWarningNotification,
                _ => ClearCache());
        }

        public void ClearCache()
        {
            _imageCache.RemoveAllObjects();
        }

        public UIImage? RenderPage(PdfDocument? document, uint pageIndex, CGSize targetSize, PageAppearance? appearance)
        {
            if (document == null || pageIndex >= document.PageCount)
                return null;

            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                var screenBounds = UIScreen.MainScreen.Bounds;
                targetSize = screenBounds.Size;
            }

            var isDark = appearance?.IsDarkMode == true;
            var key = new NSString($"{pageIndex}_{(int)targetSize.Width}x{(int)targetSize.Height}_{(isDark ? "dark" : "light")}");
            var cached = _imageCache.ObjectForKey(key) as UIImage;
            if (cached != null)
                return cached;

            var page = document.GetPage((nint)pageIndex);
            if (page == null)
                return null;

            var screenScale = UIScreen.MainScreen.Scale;
            var format = new UIGraphicsImageRendererFormat
            {
                Scale = screenScale,
                Opaque = true
            };

            var box = PdfDisplayBox.Crop;
            var pageRect = page.GetBoundsForBox(box);

            var renderer = new UIGraphicsImageRenderer(targetSize, format);
            var image = renderer.CreateImage(ctx =>
            {
                var cgContext = ctx.CGContext;

                // Fill background (white or appearance background)
                cgContext.SetFillColor(UIColor.White.CGColor);
                cgContext.FillRect(new CGRect(CGPoint.Empty, targetSize));

                if (pageRect.Width <= 0 || pageRect.Height <= 0)
                    return;

                // Calculate aspect fit scale within targetSize
                var scaleX = targetSize.Width / pageRect.Width;
                var scaleY = targetSize.Height / pageRect.Height;
                var scale = (nfloat)Math.Min(scaleX, scaleY);

                var fittedWidth = pageRect.Width * scale;
                var fittedHeight = pageRect.Height * scale;
                var offsetX = (targetSize.Width - fittedWidth) / 2.0;
                var offsetY = (targetSize.Height - fittedHeight) / 2.0;

                // CoreGraphics coordinate transformation for PDF rendering
                cgContext.SaveState();
                cgContext.TranslateCTM((nfloat)offsetX, (nfloat)(offsetY + fittedHeight));
                cgContext.ScaleCTM(scale, -scale);
                cgContext.TranslateCTM(-pageRect.X, -pageRect.Y);

                page.Draw(box, cgContext);
                cgContext.RestoreState();

                if (isDark)
                {
                    cgContext.SetBlendMode(CGBlendMode.Difference);
                    cgContext.SetFillColor(UIColor.FromRGBA(0.88f, 0.88f, 0.90f, 1.0f).CGColor);
                    cgContext.FillRect(new CGRect(CGPoint.Empty, targetSize));
                }
            });

            _imageCache.SetObjectForKey(image, key);
            return image;
        }

        public void Dispose()
        {
            if (_memoryWarningObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_memoryWarningObserver);
                _memoryWarningObserver = null;
            }
            ClearCache();
        }
    }
}
