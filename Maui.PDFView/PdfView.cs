using System.ComponentModel;
using System.Windows.Input;

namespace Maui.PDFView
{
    public class PdfView : View, IPdfView
    {
        public static readonly BindableProperty UriProperty = BindableProperty.Create(
                propertyName: nameof(Uri),
                returnType: typeof(string),
                declaringType: typeof(PdfView),
                defaultValue: default(string));

        public static readonly BindableProperty IsHorizontalProperty = BindableProperty.Create(
                propertyName: nameof(IsHorizontal),
                returnType: typeof(bool),
                declaringType: typeof(PdfView),
                defaultValue: false);

        public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
                propertyName: nameof(MaxZoom),
                returnType: typeof(float),
                declaringType: typeof(PdfView),
                defaultValue: 4f,
                propertyChanged: OnMaxZoomPropertyChanged);
        
        public static readonly BindableProperty PageAppearanceProperty = BindableProperty.Create(
                propertyName: nameof(PageAppearance),
                returnType: typeof(PageAppearance), 
                declaringType: typeof(PdfView),
                defaultValue: null);

        public static readonly BindableProperty PageChangedCommandProperty = BindableProperty.Create(
                propertyName: nameof(PageChangedCommand),
                returnType: typeof(ICommand),
                declaringType: typeof(PdfView),
                defaultValue: default(ICommand));

        public static readonly BindableProperty PageIndexProperty = BindableProperty.Create(
                propertyName: nameof(PageIndex),
                returnType: typeof(uint),
                declaringType: typeof(PdfView),
                defaultValue: (uint)0, defaultBindingMode: BindingMode.TwoWay);

        public static readonly BindableProperty TransitionModeProperty = BindableProperty.Create(
                propertyName: nameof(TransitionMode),
                returnType: typeof(PdfTransitionMode),
                declaringType: typeof(PdfView),
                defaultValue: PdfTransitionMode.ContinuousScroll);

        public static readonly BindableProperty EnablePageCurlProperty = BindableProperty.Create(
                propertyName: nameof(EnablePageCurl),
                returnType: typeof(bool),
                declaringType: typeof(PdfView),
                defaultValue: false,
                propertyChanged: OnEnablePageCurlPropertyChanged);

        public static readonly BindableProperty DoubleSidedProperty = BindableProperty.Create(
                propertyName: nameof(DoubleSided),
                returnType: typeof(bool),
                declaringType: typeof(PdfView),
                defaultValue: false);

        public string? Uri
        {
            get => (string?)GetValue(UriProperty);
            set => SetValue(UriProperty, value);
        }

        public bool IsHorizontal
        {
            get => (bool)GetValue(IsHorizontalProperty);
            set => SetValue(IsHorizontalProperty, value);
        }

        public float MaxZoom
        {
            get => (float)GetValue(MaxZoomProperty);
            set => SetValue(MaxZoomProperty, value);
        }
        
        public PageAppearance? PageAppearance
        {
            get => (PageAppearance?)GetValue(PageAppearanceProperty);
            set => SetValue(PageAppearanceProperty, value);
        }

        public ICommand PageChangedCommand
        {
            get => (ICommand)GetValue(PageChangedCommandProperty);
            set => SetValue(PageChangedCommandProperty, value);
        }

        public uint PageIndex
        {
            get => (uint)GetValue(PageIndexProperty);
            set => SetValue(PageIndexProperty, value);
        }

        public PdfTransitionMode TransitionMode
        {
            get => (PdfTransitionMode)GetValue(TransitionModeProperty);
            set => SetValue(TransitionModeProperty, value);
        }

        public bool EnablePageCurl
        {
            get => (bool)GetValue(EnablePageCurlProperty);
            set => SetValue(EnablePageCurlProperty, value);
        }

        public bool DoubleSided
        {
            get => (bool)GetValue(DoubleSidedProperty);
            set => SetValue(DoubleSidedProperty, value);
        }

        private static void OnEnablePageCurlPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is PdfView pdfView && newValue is bool enableCurl)
            {
                pdfView.TransitionMode = enableCurl ? PdfTransitionMode.PageCurl : PdfTransitionMode.ContinuousScroll;
            }
        }

        private static void OnMaxZoomPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if ((float)newValue < 1f)
                throw new ArgumentException("PdfView: MaxZoom cannot be less than 1");
        }
    }
}
