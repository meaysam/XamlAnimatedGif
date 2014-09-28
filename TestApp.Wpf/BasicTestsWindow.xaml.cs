﻿using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using XamlAnimatedGif.Decoding;
using XamlAnimatedGif.Decompression;

namespace TestApp.Wpf
{
    public partial class BasicTestsWindow
    {
        public BasicTestsWindow()
        {
            InitializeComponent();
        }

        private void BtnBrowse_OnClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog {Filter = "GIF files|*.gif"};
            if (dlg.ShowDialog() == true)
            {
                txtFileName.Text = dlg.FileName;
            }
        }

        private async void BtnDumpFrames_OnClick(object sender, RoutedEventArgs e)
        {
            string fileName = txtFileName.Text;
            if (string.IsNullOrEmpty(fileName))
                return;

            btnDumpFrames.IsEnabled = false;
            try
            {
                await DumpFramesAsync(fileName);
            }
            finally
            {
                btnDumpFrames.IsEnabled = true;
            }
        }

        private async void BtnTestLzw_OnClick(object sender, RoutedEventArgs e)
        {
            string fileName = txtFileName.Text;
            if (string.IsNullOrEmpty(fileName))
                return;

            btnDumpFrames.IsEnabled = false;
            try
            {
                await TestLzwDecompressionAsync(fileName);
            }
            finally
            {
                btnDumpFrames.IsEnabled = true;
            }
        }


        private static async Task TestLzwDecompressionAsync(string path)
        {
            using (var fileStream = File.OpenRead(path))
            {
                var gif = await GifDataStream.ReadAsync(fileStream);
                var firstFrame = gif.Frames[0];
                fileStream.Seek(firstFrame.ImageData.CompressedDataStartOffset, SeekOrigin.Begin);
                using (var dataBlockStream = new GifDataBlockStream(fileStream))
                using (var lzwStream = new LzwDecompressStream(dataBlockStream, firstFrame.ImageData.LzwMinimumCodeSize))
                using (var indOutStream = File.OpenWrite(path + ".ind"))
                {
                    await lzwStream.CopyToAsync(indOutStream);
                }
            }
        }

        static async Task MakeImageAsync(string path)
        {
            using (var fileStream = File.OpenRead(path))
            {
                var gif = await GifDataStream.ReadAsync(fileStream);
                var firstFrame = gif.Frames[0];
                var colorTable = firstFrame.LocalColorTable ?? gif.GlobalColorTable;
                var colors = colorTable.Select(gc => Color.FromRgb(gc.R, gc.G, gc.B)).ToArray();
                var palette = new BitmapPalette(colors);
                var desc = gif.Header.LogicalScreenDescriptor;
                var image = new WriteableBitmap(
                    desc.Width, desc.Height,
                    96, 96,
                    PixelFormats.Indexed8,
                    palette);

                fileStream.Seek(firstFrame.ImageData.CompressedDataStartOffset, SeekOrigin.Begin);
                var data = await GifHelpers.ReadDataBlocksAsync(fileStream, false);
                using (var ms = new MemoryStream(data))
                using (var lzwStream = new LzwDecompressStream(ms, firstFrame.ImageData.LzwMinimumCodeSize))
                using (var indexStream = new MemoryStream())
                {
                    await lzwStream.CopyToAsync(indexStream);

                    var pixelData = indexStream.ToArray();
                    image.Lock();

                    var fd = firstFrame.Descriptor;
                    var rect = new Int32Rect(fd.Left, fd.Top, fd.Width, fd.Height);
                    image.WritePixels(rect, pixelData, fd.Width, 0);
                    image.AddDirtyRect(rect);
                    image.Unlock();

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var fs = File.OpenWrite(path + ".png"))
                    {
                        encoder.Save(fs);
                    }
                }
            }
        }

        static async Task DumpFramesAsync(string path)
        {
            using (var fileStream = File.OpenRead(path))
            {
                var gif = await GifDataStream.ReadAsync(fileStream);
                var desc = gif.Header.LogicalScreenDescriptor;
                var colors = gif.GlobalColorTable.Select(gc => Color.FromRgb(gc.R, gc.G, gc.B)).ToArray();
                //colors[0] = Colors.Transparent;
                //colors[desc.BackgroundColorIndex] = Colors.Transparent;
                var gce = gif.Frames[0].Extensions.OfType<GifGraphicControlExtension>().FirstOrDefault();
                if (gce != null && gce.HasTransparency)
                {
                    colors[gce.TransparencyIndex] = Colors.Transparent;
                }
                var palette = new BitmapPalette(colors);
                var image = new WriteableBitmap(
                    desc.Width, desc.Height,
                    96, 96,
                    PixelFormats.Indexed8,
                    palette);

                for (int i = 0; i < gif.Frames.Count; i++)
                {
                    var frame = gif.Frames[i];
                    fileStream.Seek(frame.ImageData.CompressedDataStartOffset, SeekOrigin.Begin);
                    //var data = await GifHelpers.ReadDataBlocksAsync(fileStream, false);
                    //using (var ms = new MemoryStream(data))
                    using (var dataBlockStream = new GifDataBlockStream(fileStream, true))
                    using (var lzwStream = new LzwDecompressStream(dataBlockStream, frame.ImageData.LzwMinimumCodeSize))
                    using (var indexStream = new MemoryStream())
                    {
                        await lzwStream.CopyToAsync(indexStream);

                        var pixelData = indexStream.ToArray();
                        image.Lock();
                        var fd = frame.Descriptor;
                        var rect = new Int32Rect(fd.Left, fd.Top, fd.Width, fd.Height);
                        image.WritePixels(rect, pixelData, fd.Width, 0);
                        image.AddDirtyRect(rect);
                        image.Unlock();

                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        string outPath = string.Format("{0}.{1}.png", path, i);
                        using (var outStream = File.OpenWrite(outPath))
                        {
                            encoder.Save(outStream);
                        }
                    }
                }
            }
        }
    }
}