/* Copyright (c) 2007 Ben Howell
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a 
 * copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, including without limitation 
 * the rights to use, copy, modify, merge, publish, distribute, sublicense, 
 * and/or sell copies of the Software, and to permit persons to whom the 
 * Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in 
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
 * DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

using GoArrow;

using ICSharpCode.SharpZipLib.Zip;
using GoArrow.Huds;

namespace MapSplitter {
	static class Program {
		const int TileSize = 256;
		const int TilePadding = 4;
		private static readonly Color Clear = Color.FromArgb(0);
		
		[STAThread]
		static void Main() {
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			Bitmap map = Properties.Resources.DerethMapDark;
			DirectoryInfo baseDir = new DirectoryInfo("DerethMap");
			if (baseDir.Exists)
				baseDir.Delete(true);
			baseDir.Create();
			string basePath = baseDir.FullName;

			TextWriter mapTxt = new StreamWriter(File.Create(Path.Combine(basePath, "map.txt")));
			mapTxt.WriteLine(map.Width.ToString());
			mapTxt.WriteLine(TileSize.ToString());
			mapTxt.WriteLine(TilePadding.ToString());
			mapTxt.Dispose();

			Bitmap lowRes = new Bitmap((int)Math.Ceiling(map.Width / 2.0), (int)Math.Ceiling(map.Height / 2.0), PixelFormat.Format32bppArgb);
			Graphics resizer = Graphics.FromImage(lowRes);
			resizer.InterpolationMode = InterpolationMode.HighQualityBicubic;
			resizer.DrawImage(map, new Rectangle(new Point(0, 0), lowRes.Size));
			lowRes.Save(Path.Combine(basePath, "lowres.png"));

			TileGen(map, 1, TileSize, TilePadding, basePath, "{0},{1}.png");

			if (File.Exists("DerethMap.zip"))
				File.Delete("DerethMap.zip");
			ZipOutputStream zip = new ZipOutputStream(File.Create("DerethMap.zip"));
			zip.Password = "";
			foreach (FileInfo file in baseDir.GetFiles()) {
				ZipEntry ze = new ZipEntry(file.Name);
				zip.PutNextEntry(ze);
				FileStream rdr = file.OpenRead();
				byte[] buffer = new byte[rdr.Length];
				rdr.Read(buffer, 0, buffer.Length);
				rdr.Dispose();
				zip.Write(buffer, 0, buffer.Length);
				zip.CloseEntry();
			}
			zip.Close();

			System.Media.SystemSounds.Asterisk.Play();
		}

		static void TileGen(Bitmap srcBitmap, float srcZoomFactor, int tileSize, int tilePadding,
				string dstBasePath, string dstFileNameFormat) {

			if (srcZoomFactor != 1.0f) {
				int w = (int)(srcBitmap.Width * srcZoomFactor);
				int h = (int)(srcBitmap.Height * srcZoomFactor);
				Bitmap newSource = new Bitmap(w, h);
				Graphics g = Graphics.FromImage(newSource);
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.DrawImage(srcBitmap, 0, 0, w, h);
				srcBitmap = newSource;
			}

			Bitmap tile = new Bitmap(tileSize + tilePadding, tileSize + tilePadding, srcBitmap.PixelFormat);
			for (int x = 0, iX = 0; x < srcBitmap.Width; x += tileSize, iX++) {
				for (int y = 0, iY = 0; y < srcBitmap.Height; y += tileSize, iY++) {
					Graphics.FromImage(tile).Clear(Clear);
					GraphicsUtil.BitBlt(srcBitmap, x, y, tileSize + tilePadding, tileSize + tilePadding, tile, 0, 0);
					string savePath = Path.Combine(dstBasePath, string.Format(dstFileNameFormat, iX, iY));
					DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(savePath));
					if (!dir.Exists)
						dir.Create();
					tile.Save(savePath);
				}
			}
		}
	}
}