using System.ComponentModel.DataAnnotations;

namespace YMM4ModulatorPlugin
{
    public enum DrawDirection
    {
        [Display(Name = "左→右", Description = "左から右へ走査")]
        LeftToRight,
        [Display(Name = "右→左", Description = "右から左へ走査")]
        RightToLeft,
        [Display(Name = "上→下", Description = "上から下へ走査")]
        TopToBottom,
        [Display(Name = "下→上", Description = "下から上へ走査")]
        BottomToTop,
    }
}