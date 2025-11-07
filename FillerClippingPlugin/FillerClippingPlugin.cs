using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace FillerClippingPlugin.Effects
{
    [VideoEffect("間詰め分断", ["合成"], ["clipping", "crop", "切り抜き", "間詰め", "間詰め分断"], isAviUtlSupported: false, isEffectItemSupported: false)]
    internal class FillerClippingEffect : VideoEffectBase
    {
        public override string Label => $"間詰め分断 移動{XOffset.GetValue(0, 1, 30):F0}px, 分断幅{ClipWidth.GetValue(0, 1, 30):F0}px, 角度{Rotation.GetValue(0, 1, 30):F0}°, ずらし距離{Gap.GetValue(0, 1, 30):F0}px, ずらし角度{GapAngle.GetValue(0, 1, 30):F2}°";

        [Display(GroupName = "分断", Name = "分断幅", Description = "切り抜く帯状部分の幅")]
        [AnimationSlider("F1", "px", 0, 500)]
        public Animation ClipWidth { get; } = new Animation(100, 0, 100000);

        [Display(GroupName = "分断", Name = "角度", Description = "分断線の回転角度")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation Rotation { get; } = new Animation(0, -36000, 36000);

        [Display(GroupName = "分断", Name = "分断位置", Description = "分断線のオフセット")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation XOffset { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "ズレ", Name = "ズレ", Description = "分断後のアイテムのズレ")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Gap { get; } = new Animation(0, -100000, 100000);

        [Display(GroupName = "ズレ", Name = "ズレ角度", Description = "分断後のアイテムのズレで動く角度")]
        [AnimationSlider("F1", "°", -360, 360)]
        public Animation GapAngle { get; } = new Animation(0, -36000, 36000);

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new FillerClippingEffectProcessor(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [XOffset, ClipWidth, Rotation, Gap, GapAngle];
    }
}