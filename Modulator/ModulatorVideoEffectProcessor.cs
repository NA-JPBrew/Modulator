using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace YMM4ModulatorPlugin
{
    internal class ModulatorVideoEffectProcessor : IVideoEffectProcessor
    {
        DisposeCollector disposer = new();

        private readonly ModulatorVideoEffect _effect;
        private readonly IGraphicsDevicesAndContext _devices;
        private ID2D1Bitmap1? _inputBitmap;
        private ID2D1Bitmap1? _outputBitmap;
        private byte[]? _dstBuffer;
        private int _dstStride;

        ID2D1Image? _input;

        AffineTransform2D transform2D;
        bool isFirst = true;
        float transformX;
        float transformY;

        public ID2D1Image Output { get; }

        public ModulatorVideoEffectProcessor(ModulatorVideoEffect effect, IGraphicsDevicesAndContext devices)
        {
            _effect = effect;
            _devices = devices;

            transform2D = new AffineTransform2D(_devices.DeviceContext);
            disposer.Collect(transform2D);

            Output = transform2D.Output;
            disposer.Collect(Output);
        }

        public void SetInput(ID2D1Image? input)
        {
            _input = input;       
        }

        public void ClearInput()
        {
            transform2D.SetInput(0, null, true);
        }

        //public IDisposable CreateResourceSet(IGraphicsDevicesAndContext devices) => null;

        //public void UpdateDevice(IDisposable resourceSet, IGraphicsDevicesAndContext devices) { }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            // inputを_inputBitmapに描画する
            var dc = _devices.DeviceContext;

            var bounds = dc.GetImageLocalBounds(_input);
            var inputWidth = (int)(bounds.Right - bounds.Left);
            var inputHeight = (int)(bounds.Bottom - bounds.Top);
            var size = new SizeI(inputWidth, inputHeight);

            var pixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
            var bitmapProperties = new BitmapProperties1(pixelFormat, 96f, 96f, BitmapOptions.Target);

            using var bitmap = _devices.DeviceContext.CreateBitmap(size, IntPtr.Zero, 0, bitmapProperties);

            dc.Target = bitmap;
            dc.BeginDraw();
            dc.Clear(new Color4(0, 0, 0, 0));
            if (_input is not null) dc.DrawImage(_input, new Vector2(-bounds.Left, -bounds.Top));
            dc.EndDraw();
            dc.Target = null;

            var cpuPixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied);
            var cpuBitmapProperties = new BitmapProperties1(pixelFormat, 96f, 96f, BitmapOptions.CpuRead | BitmapOptions.CannotDraw);

            if (_inputBitmap is not null)
            {
                disposer.RemoveAndDispose(ref _inputBitmap);
            }

            _inputBitmap = dc.CreateBitmap(size, IntPtr.Zero, 0, cpuBitmapProperties);
            disposer.Collect(_inputBitmap);

            _inputBitmap.CopyFromBitmap(bitmap);

            // パラメータ取得
            var frame = effectDescription.ItemPosition.Frame;
            var length = effectDescription.ItemDuration.Frame;
            var fps = effectDescription.FPS;
            float scale = (float)_effect.Scale.GetValue(frame, length, fps);
            int drawing = (int)_effect.DrawingCount.GetValue(frame, length, fps);
            int interval = (int)_effect.IntervalCount.GetValue(frame, length, fps);
            int lineWidth = (int)_effect.LineWidth.GetValue(frame, length, fps);
            int threshold = (int)_effect.Threshold.GetValue(frame, length, fps);
            int opaWhite = (int)_effect.OpacityWhite.GetValue(frame, length, fps);
            int opaBlack = (int)_effect.OpacityBlack.GetValue(frame, length, fps);
            bool invert = _effect.IsInvert;
            DrawDirection direction = _effect.Direction;

            // 出力ビットマップの準備（固定形式で作成）
            PrepareOutputBitmap(_inputBitmap);

            try
            {
                var srcMapped = _inputBitmap.Map(MapOptions.Read);
                try
                {
                    int w = _inputBitmap.PixelSize.Width;
                    int h = _inputBitmap.PixelSize.Height;
                    int srcStride = srcMapped.Pitch;

                    _dstStride = w * 4;
                    int requiredBytes = _dstStride * h;
                    if (_dstBuffer == null || _dstBuffer.Length != requiredBytes)
                        _dstBuffer = new byte[requiredBytes];

                    unsafe
                    {
                        byte* srcPixels = (byte*)srcMapped.Bits;
                        fixed (byte* dstPixels = _dstBuffer)
                        {
                            ApplyModulator(srcPixels, dstPixels, w, h, srcStride, _dstStride,
                                           scale, threshold, drawing, interval, lineWidth,
                                           direction, invert, opaWhite, opaBlack);
                        }
                    }

                    _outputBitmap.CopyFromMemory(_dstBuffer, _dstStride);
                }
                finally
                {
                    _inputBitmap.Unmap();
                }
            }
            catch
            {
                // 失敗時は何もしない
            }

            transform2D.SetInput(0, _outputBitmap, true);

            var transformX = bounds.Left;
            var transformY = bounds.Top;

            if (isFirst || this.transformX != transformX || this.transformY != transformY)
            {
                transform2D.TransformMatrix = Matrix3x2.CreateTranslation(new Vector2(transformX, transformY));
                this.transformX = transformX;
                this.transformY = transformY;
            }

            isFirst = false;


            return effectDescription.DrawDescription;
        }

        [MemberNotNull(nameof(_outputBitmap))]
        private void PrepareOutputBitmap(ID2D1Bitmap1 sourceBitmap)
        {
            var size = sourceBitmap.PixelSize;

            // 既存の出力ビットマップが同じサイズなら再利用
            if (_outputBitmap != null &&
                _outputBitmap.PixelSize.Width == size.Width &&
                _outputBitmap.PixelSize.Height == size.Height)
            {
                return;
            }

            var props = new BitmapProperties1
            {
                BitmapOptions = BitmapOptions.None, // CPU書き込み不可能、描画可能
                PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied)
            };

            if (_outputBitmap is not null)
            {
                disposer.RemoveAndDispose(ref  _outputBitmap);
            }

            _outputBitmap = _devices.DeviceContext.CreateBitmap(size, props);
            disposer.Collect(_outputBitmap);

            _dstStride = size.Width * 4;
            _dstBuffer = new byte[_dstStride * size.Height];
        }

        private unsafe void ApplyModulator(byte* srcPixels, byte* dstPixels, int w, int h,
                                           int srcStride, int dstStride,
                                           float scale, int threshold,
                                           int drawingCount, int intervalCount, int lineWidth,
                                           DrawDirection direction, bool invert,
                                           int opaWhite, int opaBlack)
        {
            switch (direction)
            {
                case DrawDirection.LeftToRight:
                    ProcessHorizontal(srcPixels, dstPixels, w, h, srcStride, dstStride,
                                      scale, threshold, drawingCount, intervalCount, lineWidth,
                                      invert, opaWhite, opaBlack);
                    break;
                case DrawDirection.RightToLeft:
                    ProcessHorizontalReverse(srcPixels, dstPixels, w, h, srcStride, dstStride,
                                             scale, threshold, drawingCount, intervalCount, lineWidth,
                                             invert, opaWhite, opaBlack);
                    break;
                case DrawDirection.TopToBottom:
                    ProcessVertical(srcPixels, dstPixels, w, h, srcStride, dstStride,
                                    scale, threshold, drawingCount, intervalCount, lineWidth,
                                    invert, opaWhite, opaBlack);
                    break;
                case DrawDirection.BottomToTop:
                    ProcessVerticalReverse(srcPixels, dstPixels, w, h, srcStride, dstStride,
                                           scale, threshold, drawingCount, intervalCount, lineWidth,
                                           invert, opaWhite, opaBlack);
                    break;
            }
        }

        private unsafe void ProcessHorizontal(byte* srcPixels, byte* dstPixels, int w, int h,
                                              int srcStride, int dstStride,
                                              float scale, int threshold,
                                              int drawingCount, int intervalCount, int lineWidth,
                                              bool invert, int opaWhite, int opaBlack)
        {
            for (int y = 0; y < h; y++)
            {
                byte* srcRow = srcPixels + y * srcStride;
                byte* dstRow = dstPixels + y * dstStride;
                double lumisum = 0;
                int drawRemain = 0;
                int drawingRemain = drawingCount;
                int intervalRemain = 0;

                for (int x = 0; x < w; x++)
                {
                    byte* srcPixel = srcRow + x * 4;
                    byte* dstPixel = dstRow + x * 4;

                    double lumi = (srcPixel[0] + srcPixel[1] + srcPixel[2]) / 3.0;
                    lumisum += invert ? lumi / scale : (255.0 - lumi) / scale;

                    if (lumisum >= threshold)
                    {
                        lumisum = 0;
                        if (intervalRemain > 0)
                        {
                            intervalRemain--;
                        }
                        else if (drawingRemain > 0)
                        {
                            drawRemain = lineWidth;
                            drawingRemain--;
                            if (drawingRemain <= 0 && intervalCount > 0)
                            {
                                intervalRemain = intervalCount;
                                drawingRemain = drawingCount;
                            }
                        }
                    }

                    if (drawRemain > 0)
                    {
                        dstPixel[0] = 255;
                        dstPixel[1] = 255;
                        dstPixel[2] = 255;
                        dstPixel[3] = (byte)((srcPixel[3] * opaWhite) / 255);
                        drawRemain--;
                    }
                    else
                    {
                        dstPixel[0] = 0;
                        dstPixel[1] = 0;
                        dstPixel[2] = 0;
                        dstPixel[3] = (byte)((srcPixel[3] * opaBlack) / 255);
                    }
                }
            }
        }

        private unsafe void ProcessHorizontalReverse(byte* srcPixels, byte* dstPixels, int w, int h,
                                                     int srcStride, int dstStride,
                                                     float scale, int threshold,
                                                     int drawingCount, int intervalCount, int lineWidth,
                                                     bool invert, int opaWhite, int opaBlack)
        {
            for (int y = 0; y < h; y++)
            {
                byte* srcRow = srcPixels + y * srcStride;
                byte* dstRow = dstPixels + y * dstStride;
                double lumisum = 0;
                int drawRemain = 0;
                int drawingRemain = drawingCount;
                int intervalRemain = 0;

                for (int x = w - 1; x >= 0; x--)
                {
                    byte* srcPixel = srcRow + x * 4;
                    byte* dstPixel = dstRow + x * 4;

                    double lumi = (srcPixel[0] + srcPixel[1] + srcPixel[2]) / 3.0;
                    lumisum += invert ? lumi / scale : (255.0 - lumi) / scale;

                    if (lumisum >= threshold)
                    {
                        lumisum = 0;
                        if (intervalRemain > 0)
                        {
                            intervalRemain--;
                        }
                        else if (drawingRemain > 0)
                        {
                            drawRemain = lineWidth;
                            drawingRemain--;
                            if (drawingRemain <= 0 && intervalCount > 0)
                            {
                                intervalRemain = intervalCount;
                                drawingRemain = drawingCount;
                            }
                        }
                    }

                    if (drawRemain > 0)
                    {
                        dstPixel[0] = 255;
                        dstPixel[1] = 255;
                        dstPixel[2] = 255;
                        dstPixel[3] = (byte)((srcPixel[3] * opaWhite) / 255);
                        drawRemain--;
                    }
                    else
                    {
                        dstPixel[0] = 0;
                        dstPixel[1] = 0;
                        dstPixel[2] = 0;
                        dstPixel[3] = (byte)((srcPixel[3] * opaBlack) / 255);
                    }
                }
            }
        }

        private unsafe void ProcessVertical(byte* srcPixels, byte* dstPixels, int w, int h,
                                            int srcStride, int dstStride,
                                            float scale, int threshold,
                                            int drawingCount, int intervalCount, int lineWidth,
                                            bool invert, int opaWhite, int opaBlack)
        {
            for (int x = 0; x < w; x++)
            {
                double lumisum = 0;
                int drawRemain = 0;
                int drawingRemain = drawingCount;
                int intervalRemain = 0;

                for (int y = 0; y < h; y++)
                {
                    byte* srcPixel = srcPixels + y * srcStride + x * 4;
                    byte* dstPixel = dstPixels + y * dstStride + x * 4;

                    double lumi = (srcPixel[0] + srcPixel[1] + srcPixel[2]) / 3.0;
                    lumisum += invert ? lumi / scale : (255.0 - lumi) / scale;

                    if (lumisum >= threshold)
                    {
                        lumisum = 0;
                        if (intervalRemain > 0)
                        {
                            intervalRemain--;
                        }
                        else if (drawingRemain > 0)
                        {
                            drawRemain = lineWidth;
                            drawingRemain--;
                            if (drawingRemain <= 0 && intervalCount > 0)
                            {
                                intervalRemain = intervalCount;
                                drawingRemain = drawingCount;
                            }
                        }
                    }

                    if (drawRemain > 0)
                    {
                        dstPixel[0] = 255;
                        dstPixel[1] = 255;
                        dstPixel[2] = 255;
                        dstPixel[3] = (byte)((srcPixel[3] * opaWhite) / 255);
                        drawRemain--;
                    }
                    else
                    {
                        dstPixel[0] = 0;
                        dstPixel[1] = 0;
                        dstPixel[2] = 0;
                        dstPixel[3] = (byte)((srcPixel[3] * opaBlack) / 255);
                    }
                }
            }
        }

        private unsafe void ProcessVerticalReverse(byte* srcPixels, byte* dstPixels, int w, int h,
                                                   int srcStride, int dstStride,
                                                   float scale, int threshold,
                                                   int drawingCount, int intervalCount, int lineWidth,
                                                   bool invert, int opaWhite, int opaBlack)
        {
            for (int x = 0; x < w; x++)
            {
                double lumisum = 0;
                int drawRemain = 0;
                int drawingRemain = drawingCount;
                int intervalRemain = 0;

                for (int y = h - 1; y >= 0; y--)
                {
                    byte* srcPixel = srcPixels + y * srcStride + x * 4;
                    byte* dstPixel = dstPixels + y * dstStride + x * 4;

                    double lumi = (srcPixel[0] + srcPixel[1] + srcPixel[2]) / 3.0;
                    lumisum += invert ? lumi / scale : (255.0 - lumi) / scale;

                    if (lumisum >= threshold)
                    {
                        lumisum = 0;
                        if (intervalRemain > 0)
                        {
                            intervalRemain--;
                        }
                        else if (drawingRemain > 0)
                        {
                            drawRemain = lineWidth;
                            drawingRemain--;
                            if (drawingRemain <= 0 && intervalCount > 0)
                            {
                                intervalRemain = intervalCount;
                                drawingRemain = drawingCount;
                            }
                        }
                    }

                    if (drawRemain > 0)
                    {
                        dstPixel[0] = 255;
                        dstPixel[1] = 255;
                        dstPixel[2] = 255;
                        dstPixel[3] = (byte)((srcPixel[3] * opaWhite) / 255);
                        drawRemain--;
                    }
                    else
                    {
                        dstPixel[0] = 0;
                        dstPixel[1] = 0;
                        dstPixel[2] = 0;
                        dstPixel[3] = (byte)((srcPixel[3] * opaBlack) / 255);
                    }
                }
            }
        }

        public void Dispose()
        {
            disposer.Dispose();
        }
    }
}