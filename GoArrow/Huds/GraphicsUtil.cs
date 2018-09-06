/* Copyright (c) 2007 Ben Howell
 * This software is licensed under the MIT License
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
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace GoArrow.Huds
{
	public static class GraphicsUtil
	{
		/// <summary>
		/// Copies image onto another. Both images must have the same pixel format.
		/// </summary>
		/// <param name="srcImg">The image to copy from.</param>
		/// <param name="dstImg">The image to copy onto.</param>
		public static void BitBlt(Bitmap srcImg, Bitmap dstImg)
		{
			BitBlt(srcImg, 0, 0, srcImg.Width, srcImg.Height, dstImg, 0, 0);
		}

		/// <summary>
		/// Copies image onto another. Both images must have the same pixel format.
		/// </summary>
		/// <param name="srcImg">The image to copy from.</param>
		/// <param name="dstImg">The image to copy onto.</param>
		/// <param name="dstPt">The point on the destination image to copy to.</param>
		public static void BitBlt(Bitmap srcImg, Bitmap dstImg, Point dstPt)
		{
			BitBlt(srcImg, 0, 0, srcImg.Width, srcImg.Height, dstImg, dstPt.X, dstPt.Y);
		}

		/// <summary>
		/// Copies image onto another. Both images must have the same pixel format.
		/// </summary>
		/// <param name="srcImg">The image to copy from.</param>
		/// <param name="dstImg">The image to copy onto.</param>
		/// <param name="dX">The x-coordiate of the destiation rectangle.</param>
		/// <param name="dY">The y-coordiate of the destiation rectangle.</param>
		public static void BitBlt(Bitmap srcImg, Bitmap dstImg, int dX, int dY)
		{
			BitBlt(srcImg, 0, 0, srcImg.Width, srcImg.Height, dstImg, dX, dY);
		}

		/// <summary>
		/// Copies part of one image onto another. Both images must have the 
		/// same pixel format.
		/// </summary>
		/// <param name="srcImg">The image to copy from.</param>
		/// <param name="srcRect">The rectangle on the source image to copy from.</param>
		/// <param name="dstImg">The image to copy onto.</param>
		/// <param name="dstPt">The point on the destination image to copy to.</param>
		public static void BitBlt(Bitmap srcImg, Rectangle srcRect, Bitmap dstImg, Point dstPt)
		{
			BitBlt(srcImg, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, dstImg, dstPt.X, dstPt.Y);
		}

		/// <summary>
		/// Copies part of one image onto another. Both images must have the 
		/// same pixel format.
		/// </summary>
		/// <param name="srcImg">The image to copy from.</param>
		/// <param name="sX">The x-coordinate of the source rectangle.</param>
		/// <param name="sY">The y-coordinate to the source rectangle.</param>
		/// <param name="w">The width of the region to copy.</param>
		/// <param name="h">The height of the region to copy.</param>
		/// <param name="dstImg">The image to copy onto.</param>
		/// <param name="dX">The x-coordiate of the destiation rectangle.</param>
		/// <param name="dY">The y-coordiate of the destiation rectangle.</param>
		/// <exception cref="ArgumentException">The source and destination pixel 
		///		formats don't match, or aren't a supported format.</exception>
		public static void BitBlt(Bitmap srcImg, int sX, int sY, int w, int h, Bitmap dstImg, int dX, int dY)
		{
			if (srcImg.PixelFormat != dstImg.PixelFormat)
			{
				throw new ArgumentException("Source and destination images must have the same pixel format");
			}

			// Copy the palette for indexed images
			if ((srcImg.PixelFormat & PixelFormat.Indexed) != 0)
			{
				dstImg.Palette = srcImg.Palette;
			}

			// Adjust bounds if they go outside the images
			if (sX < 0) { w += sX; dX -= sX; sX = 0; }
			if (sY < 0) { h += sY; dY -= sY; sY = 0; }
			if (dX < 0) { w += dX; sX -= dX; dX = 0; }
			if (dY < 0) { h += dY; sY -= dY; dY = 0; }
			if (sX + w > srcImg.Width) { w = srcImg.Width - sX; }
			if (sY + h > srcImg.Height) { h = srcImg.Height - sY; }
			if (dX + w > dstImg.Width) { w = dstImg.Width - dX; }
			if (dY + h > dstImg.Height) { h = dstImg.Height - dY; }

			if (w <= 0 || h <= 0 || sX > srcImg.Width || sY > srcImg.Height
					|| dX > dstImg.Width || dY > dstImg.Height)
			{
				// Nothing to do
				return;
			}

			BitmapData srcData = srcImg.LockBits(new Rectangle(sX, sY, w, h), ImageLockMode.ReadOnly, srcImg.PixelFormat);
			BitmapData dstData = dstImg.LockBits(new Rectangle(dX, dY, w, h), ImageLockMode.WriteOnly, dstImg.PixelFormat);
			try
			{
				unsafe
				{
					if (srcImg.PixelFormat == PixelFormat.Format8bppIndexed)
					{

						// 1 byte per pixel
						byte* pSrc = (byte*)srcData.Scan0;
						byte* pDst = (byte*)dstData.Scan0;

						int srcRemain = srcData.Stride - w;
						int dstRemain = dstData.Stride - w;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								*pDst = *pSrc;
								pSrc++;
								pDst++;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else if (srcImg.PixelFormat == PixelFormat.Format16bppArgb1555
						 || srcImg.PixelFormat == PixelFormat.Format16bppGrayScale
						 || srcImg.PixelFormat == PixelFormat.Format16bppRgb555
						 || srcImg.PixelFormat == PixelFormat.Format16bppRgb565)
					{

						// 2 bytes per pixel
						Int16* pSrc = (Int16*)srcData.Scan0;
						Int16* pDst = (Int16*)dstData.Scan0;

						int srcRemain = srcData.Stride / 2 - w;
						int dstRemain = dstData.Stride / 2 - w;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								*pDst = *pSrc;
								pSrc++;
								pDst++;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else if (srcImg.PixelFormat == PixelFormat.Format24bppRgb)
					{

						// 3 bytes per pixel
						byte* pSrc = (byte*)srcData.Scan0;
						byte* pDst = (byte*)dstData.Scan0;

						int srcRemain = srcData.Stride - w * 3;
						int dstRemain = dstData.Stride - w * 3;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								pDst[0] = pSrc[0];
								pDst[1] = pSrc[1];
								pDst[2] = pSrc[2];
								pSrc += 3;
								pDst += 3;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else if (srcImg.PixelFormat == PixelFormat.Format32bppArgb
					   || srcImg.PixelFormat == PixelFormat.Format32bppPArgb)
					{

						// 4 bytes per pixel
						Int32* pSrc = (Int32*)srcData.Scan0;
						Int32* pDst = (Int32*)dstData.Scan0;

						int srcRemain = srcData.Stride / 4 - w;
						int dstRemain = dstData.Stride / 4 - w;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								*pDst = *pSrc;
								pSrc++;
								pDst++;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else if (srcImg.PixelFormat == PixelFormat.Format48bppRgb)
					{

						// 6 bytes per pixel
						byte* pSrc = (byte*)srcData.Scan0;
						byte* pDst = (byte*)dstData.Scan0;

						int srcRemain = srcData.Stride - w * 6;
						int dstRemain = dstData.Stride - w * 6;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								pDst[0] = pSrc[0];
								pDst[1] = pSrc[1];
								pDst[2] = pSrc[2];
								pDst[3] = pSrc[3];
								pDst[4] = pSrc[4];
								pDst[5] = pSrc[5];
								pSrc += 6;
								pDst += 6;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else if (srcImg.PixelFormat == PixelFormat.Format64bppArgb)
					{

						// 8 bytes per pixel
						Int64* pSrc = (Int64*)srcData.Scan0;
						Int64* pDst = (Int64*)dstData.Scan0;

						int srcRemain = srcData.Stride / 8 - w;
						int dstRemain = dstData.Stride / 8 - w;
						for (int i = 0; i < h; i++)
						{
							for (int j = 0; j < w; j++)
							{
								*pDst = *pSrc;
								pSrc++;
								pDst++;
							}
							pSrc += srcRemain;
							pDst += dstRemain;
						}
					}
					else
					{
						throw new ArgumentException("Pixel format \"" + srcImg.PixelFormat
							+ "\" is not supported");
					}
				}
			}
			finally
			{
				dstImg.UnlockBits(dstData);
				srcImg.UnlockBits(srcData);
			}
		}
	}
}
