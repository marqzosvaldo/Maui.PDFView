using System.Windows.Input;

namespace Maui.PDFView
{
    public interface IPdfView : IView
    {
        string? Uri { get; set; }
        bool IsHorizontal { get; set; }
        float MaxZoom { get; set; }
        PageAppearance? PageAppearance { get; set; }
        uint PageIndex { get; set; }

        ICommand PageChangedCommand { get; set; }
        PdfTransitionMode TransitionMode { get; set; }
        bool DoubleSided { get; set; }
        bool IsDualPage { get; set; }
        bool IsDarkMode { get; set; }
    }
}
