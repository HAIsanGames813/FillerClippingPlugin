using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;
using System;

namespace FillerClippingPlugin.Effects
{
    internal class FillerClippingEffectProcessor(IGraphicsDevicesAndContext devices, FillerClippingEffect item) : VideoEffectProcessorBase(devices)
    {
        readonly ID2D1DeviceContext deviceContext = devices.DeviceContext;

        bool isFirst = true;

        float currentXOffset = 0;
        // float currentYOffset = 0; // YOffsetを削除
        float currentClipWidth = 0;
        float currentRotation = 0;
        float currentGap = 0;
        float currentGapAngle = 0;

        // 左パーツ用:
        AffineTransform2D? leftRotationEffect; // 逆回転用 (Cropの前)
        Crop? leftCropEffect;
        AffineTransform2D? leftTransformEffect; // 順回転と分離移動用 (Cropの後)

        // 右パーツ用:
        AffineTransform2D? rightRotationEffect; // 逆回転用 (Cropの前)
        Crop? rightCropEffect;
        AffineTransform2D? rightTransformEffect; // 順回転と分離移動用 (Cropの後)

        // 最終合成用:
        Composite? finalCompositeEffect;

        protected override void setInput(ID2D1Image? input)
        {
            // 入力を回転エフェクトに設定
            leftRotationEffect?.SetInput(0, input, true);
            rightRotationEffect?.SetInput(0, input, true);
        }

        protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
        {
            // -------------------- 左パーツのエフェクトチェーン --------------------
            leftRotationEffect = new AffineTransform2D(devices.DeviceContext);
            disposer.Collect(leftRotationEffect);
            leftCropEffect = new Crop(devices.DeviceContext);
            disposer.Collect(leftCropEffect);
            leftCropEffect.BorderMode = BorderMode.Hard; // 境界線のエッジを硬く設定
            leftTransformEffect = new AffineTransform2D(devices.DeviceContext);
            disposer.Collect(leftTransformEffect);

            // チェーン: Input -> leftRotationEffect -> leftCropEffect -> leftTransformEffect -> Output
            using (var leftRotationOutput = leftRotationEffect.Output)
            {
                leftCropEffect.SetInput(0, leftRotationOutput, true);
            }
            using (var leftCropOutput = leftCropEffect.Output)
            {
                leftTransformEffect.SetInput(0, leftCropOutput, true);
            }

            // -------------------- 右パーツのエフェクトチェーン --------------------
            rightRotationEffect = new AffineTransform2D(devices.DeviceContext);
            disposer.Collect(rightRotationEffect);
            rightCropEffect = new Crop(devices.DeviceContext);
            disposer.Collect(rightCropEffect);
            rightCropEffect.BorderMode = BorderMode.Hard; // 境界線のエッジを硬く設定
            rightTransformEffect = new AffineTransform2D(devices.DeviceContext);
            disposer.Collect(rightTransformEffect);

            // チェーン: Input -> rightRotationEffect -> rightCropEffect -> rightTransformEffect -> Output
            using (var rightRotationOutput = rightRotationEffect.Output)
            {
                rightCropEffect.SetInput(0, rightRotationOutput, true);
            }
            using (var rightCropOutput = rightCropEffect.Output)
            {
                rightTransformEffect.SetInput(0, rightCropOutput, true);
            }

            // -------------------- 合成 --------------------
            finalCompositeEffect = new Composite(devices.DeviceContext);
            disposer.Collect(finalCompositeEffect);
            finalCompositeEffect.Mode = CompositeMode.SourceOver;

            using (var leftOutput = leftTransformEffect.Output)
            {
                finalCompositeEffect.SetInput(0, leftOutput, true);
            }
            using (var rightOutput = rightTransformEffect.Output)
            {
                finalCompositeEffect.SetInput(1, rightOutput, true);
            }

            var output = finalCompositeEffect.Output;
            disposer.Collect(output);
            return output;
        }

        public override DrawDescription Update(EffectDescription effectDescription)
        {
            // nullチェック
            if (leftCropEffect is null || leftTransformEffect is null || rightCropEffect is null || rightTransformEffect is null || finalCompositeEffect is null || input is null
                || leftRotationEffect is null || rightRotationEffect is null)
                return effectDescription.DrawDescription;

            var inputRect = deviceContext.GetImageLocalBounds(input);

            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;

            var xOffset = (float)item.XOffset.GetValue(frame, length, fps);
            // var yOffset = (float)item.YOffset.GetValue(frame, length, fps); // YOffsetを削除
            var clipWidth = (float)item.ClipWidth.GetValue(frame, length, fps);
            var rotation = (float)item.Rotation.GetValue(frame, length, fps);
            var gap = (float)item.Gap.GetValue(frame, length, fps);
            var gapAngle = (float)item.GapAngle.GetValue(frame, length, fps);

            var itemLeft = inputRect.Left;
            var itemTop = inputRect.Top;
            var itemRight = inputRect.Right;
            var itemBottom = inputRect.Bottom;
            var itemCenterLocalX = itemLeft + (itemRight - itemLeft) / 2f;
            var itemCenterLocalY = itemTop + (itemBottom - itemTop) / 2f;

            // 分断線（クリッピング帯）の中心
            var clipCenterX = itemCenterLocalX + xOffset;
            var clipCenterY = itemCenterLocalY; // YOffsetを削除し、アイテムの中心Y座標に固定
            var halfClipWidth = clipWidth / 2f;

            // --- 矩形定義（逆回転後の座標系に適用） ---

            const float MAX_BOUND = 5000f;

            // 逆回転後の座標系では、クリッピング境界はX軸に沿って定義される。
            var leftRect = new Vector4(-MAX_BOUND, -MAX_BOUND, -halfClipWidth, MAX_BOUND);
            var rightRect = new Vector4(halfClipWidth, -MAX_BOUND, MAX_BOUND, MAX_BOUND);

            // --- 行列の計算 ---

            var clip_half = clipWidth / 2f;
            var gap_half = gap / 2f;

            // 最終移動量の大きさ（逆回転後のX軸に沿った移動量）
            // left_move_mag: ClipWidthによる内側寄せが正
            var left_move_mag = clip_half - gap_half;
            var right_move_mag = gap_half - clip_half;

            // 回転の中心 (分断線の中心)
            var rotateCenter = new Vector2(clipCenterX, clipCenterY);
            var rad = rotation * (float)(Math.PI / 180.0);
            var rad_gap = gapAngle * (float)(Math.PI / 180.0);

            // 行列の定義 (Matrix3x2を使用)
            Matrix3x2 T_center = Matrix3x2.CreateTranslation(rotateCenter);
            Matrix3x2 T_negCenter = Matrix3x2.CreateTranslation(-rotateCenter);
            Matrix3x2 R_fwd_orig = Matrix3x2.CreateRotation(rad);
            Matrix3x2 R_inv_orig = Matrix3x2.CreateRotation(-rad);

            // ----------------------------------------------------
            // Step 1 (leftRotationEffect/rightRotationEffect): 逆回転行列を適用 (Cropより前)
            Matrix3x2 leftRotationMatrix = R_inv_orig * T_negCenter;
            Matrix3x2 rightRotationMatrix = R_inv_orig * T_negCenter;

            // ----------------------------------------------------
            // Step 2 (leftTransformEffect/rightTransformEffect): 順回転 + 明示的な斜め移動行列を適用 (Cropより後)

            // 90°ずれの移動ベクトルを使用 (X: cos, Y: sin)

            // 左パーツの最終斜め移動ベクトルを計算
            // V = M * (cos(rad_gap), sin(rad_gap))
            var left_sep_x = left_move_mag * (float)Math.Cos(rad_gap);
            var left_sep_y = left_move_mag * (float)Math.Sin(rad_gap);

            // 右パーツの最終斜め移動ベクトルを計算
            // V = M' * (cos(rad_gap), sin(rad_gap))
            var right_sep_x = right_move_mag * (float)Math.Cos(rad_gap);
            var right_sep_y = right_move_mag * (float)Math.Sin(rad_gap);

            // T_sep: 分離移動のみを行う行列
            Matrix3x2 T_left_sep = Matrix3x2.CreateTranslation(left_sep_x, left_sep_y);
            Matrix3x2 T_right_sep = Matrix3x2.CreateTranslation(right_sep_x, right_sep_y);

            // 最終行列: T_center * T_sep * R_fwd_orig
            Matrix3x2 leftTransformMatrix = T_center * T_left_sep * R_fwd_orig;
            Matrix3x2 rightTransformMatrix = T_center * T_right_sep * R_fwd_orig;
            // ----------------------------------------------------

            // 常に回転時のロジックを使用
            // currentYOffsetのチェックを削除
            if (isFirst || currentXOffset != xOffset || currentClipWidth != clipWidth || currentRotation != rotation || currentGap != gap || currentGapAngle != gapAngle)
            {
                // Crop矩形は常にMAX_BOUNDベースを使用
                leftCropEffect.Rectangle = leftRect;
                rightCropEffect.Rectangle = rightRect;

                // Step 1: 逆回転行列を適用 (Cropより前)
                leftRotationEffect.TransformMatrix = leftRotationMatrix;
                rightRotationEffect.TransformMatrix = rightRotationMatrix;

                // Step 3: 順回転 + 分離移動行列を適用 (Cropより後)
                leftTransformEffect.TransformMatrix = leftTransformMatrix;
                rightTransformEffect.TransformMatrix = rightTransformMatrix;
            }

            isFirst = false;
            currentXOffset = xOffset;
            // currentYOffset = yOffset; // YOffsetを削除
            currentClipWidth = clipWidth;
            currentRotation = rotation;
            currentGap = gap;
            currentGapAngle = gapAngle;

            return effectDescription.DrawDescription;
        }

        protected override void ClearEffectChain()
        {
            // SetInput(0, null, true)で接続を解除
            leftRotationEffect?.SetInput(0, null, true);
            leftCropEffect?.SetInput(0, null, true);
            leftTransformEffect?.SetInput(0, null, true);

            rightRotationEffect?.SetInput(0, null, true);
            rightCropEffect?.SetInput(0, null, true);
            rightTransformEffect?.SetInput(0, null, true);

            finalCompositeEffect?.SetInput(0, null, true);
            finalCompositeEffect?.SetInput(1, null, true);
        }
    }
}