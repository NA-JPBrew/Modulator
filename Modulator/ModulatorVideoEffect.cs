using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace YMM4ModulatorPlugin
{
    [VideoEffect("Modulator", new[] { "描画", "Modulator" }, new string[] { })]
    public class ModulatorVideoEffect : VideoEffectBase
    {
        public override string Label => "Modulator";

        [Display(Name = "スケール", Description = "線の間隔のスケール")]
        [AnimationSlider("F2", "", 0.25, 100)]
        public Animation Scale { get; } = new Animation(5.0, 0.25, 100);

        [Display(Name = "描画数", Description = "一束の線の数")]
        [AnimationSlider("F0", "", 1, 100)]
        public Animation DrawingCount { get; } = new Animation(10, 1, 100);

        [Display(Name = "間隔数", Description = "束と束の間の間隔（線一本分が単位）")]
        [AnimationSlider("F0", "", 0, 100)]
        public Animation IntervalCount { get; } = new Animation(2, 0, 100);

        [Display(Name = "線幅", Description = "線の太さ(px)")]
        [AnimationSlider("F0", "px", 1, 20)]
        public Animation LineWidth { get; } = new Animation(2, 1, 20);

        [Display(Name = "方向", Description = "線を描画する方向")]
        [EnumComboBox]
        public DrawDirection Direction { get; set; } = DrawDirection.LeftToRight;

        [Display(Name = "閾値", Description = "各行(列)におけるピクセルの輝度合計の閾値")]
        [AnimationSlider("F0", "", 0, 255)]
        public Animation Threshold { get; } = new Animation(255, 0, 255);

        [Display(Name = "透明度(白)", Description = "白線の透明度")]
        [AnimationSlider("F0", "", 0, 255)]
        public Animation OpacityWhite { get; } = new Animation(255, 0, 255);

        [Display(Name = "透明度(黒)", Description = "黒線の透明度")]
        [AnimationSlider("F0", "", 0, 255)]
        public Animation OpacityBlack { get; } = new Animation(255, 0, 255);

        [Display(Name = "反転", Description = "色を反転させる")]
        [ToggleSlider]
        public bool IsInvert { get; set; } = false;

        /// <summary>
        /// Exoフィルタを作成する。
        /// </summary>
        /// <param name="keyFrameIndex">キーフレーム番号</param>
        /// <param name="exoOutputDescription">exo出力に必要な各種情報</param>
        /// <returns>Exoフィルタ文字列のコレクション</returns>
        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            var fps = exoOutputDescription.VideoInfo.FPS;
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new ModulatorVideoEffectProcessor(this, devices);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables()
        {
            return [Scale, DrawingCount, IntervalCount, LineWidth, Threshold, OpacityWhite, OpacityBlack];
        }
    }
}