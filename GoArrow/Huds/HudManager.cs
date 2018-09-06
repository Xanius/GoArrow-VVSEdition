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
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

using DecalTimer = Decal.Interop.Input.TimerClass;

namespace GoArrow.Huds
{
	public class ExceptionEventArgs : EventArgs
	{
		public readonly Exception Exception;

		public ExceptionEventArgs(Exception ex)
		{
			this.Exception = ex;
		}
	}

	/// <summary>
	/// A Hud class that is managed by a HudManager must implement 
	/// this interface.
	/// </summary>
	interface IManagedHud : IDisposable
	{
		/// <summary>
		/// This function is called once per frame so the Hud can stay updated.
		/// </summary>
		void RepaintHeartbeat();

		/// <summary>
		/// This is called either when the Hud is lost because of a graphics
		/// reset, or if the Hud needs to be moved on top of other Huds. The
		/// implementation of this function should disable and remove the Hud
		/// from the render service (unless it has already been lost by a 
		/// graphics reset), then recreate the Hud right away, even if it is
		/// not enabled. This will ensure proper Z-ordering of the Huds.
		/// </summary>
		void RecreateHud();

		/// <summary>
		/// This is called whenever there is a WindowsMessage. It is necessary
		/// for Huds to use this rather than directly subscribing to the 
		/// Core.WindowMessage event so the manager can control the order in 
		/// which Huds process the messages (Huds on top process the messages
		/// first).
		/// </summary>
		void WindowMessage(WindowMessageEventArgs e);

		/// <summary>
		/// Gets the Manager for this hud, or null if this hud has not yet 
		/// registered with a manager.
		/// </summary>
		HudManager Manager { get; }

		/// <summary>
		/// This property should be true if the mouse is hovering over the hud 
		/// AND the type of Hud obscures other Huds (such as a WindowHud). This
		/// is used to help other Huds determine if the mouse is hovering on a
		/// part of them thatn is not obscured by another hud.
		/// </summary>
		bool MouseHoveringObscuresOther { get; }

		/// <summary>
		/// This property should return false until the Dispose() method is 
		/// called on the Hud.
		/// </summary>
		bool Disposed { get; }

        bool Visible { get; set; }

        event EventHandler VisibleChanged;
	}

	/// <summary>
	/// Manages the Z-ordering, graphics reset events, and WindowMessage events 
	/// of a set of WindowHuds.
	/// </summary>
	class HudManager : IDisposable
	{
		#region WindowMessage Constants
		public const short WM_MOUSEMOVE = 0x0200;
		public const short WM_LBUTTONDOWN = 0x0201;
		public const short WM_LBUTTONUP = 0x0202;
		public const short WM_RBUTTONDOWN = 0x0204;
		public const short WM_RBUTTONUP = 0x0205;
		public const short WM_MBUTTONDOWN = 0x0207;
		public const short WM_MBUTTONUP = 0x0208;
		public const short WM_MOUSEWHEEL = 0x020A;

		// Range of WM_MOUSE* events
		public const short WM_MOUSEFIRST = 0x0200;
		public const short WM_MOUSELAST = 0x020A;
		#endregion

		/// <summary>
		/// All of the non-AlwaysOnTop huds that are managed by this manager 
		/// and not disposed.
		/// </summary>
		private LinkedList<IManagedHud> mHudsList = new LinkedList<IManagedHud>();
		/// <summary>
		/// All of the AlwaysOnTop huds that are managed by this manager and 
		/// not disposed.
		/// </summary>
		private LinkedList<IManagedHud> mHudsOnTopList = new LinkedList<IManagedHud>();
		/// <summary>
		/// This is necessary for using a foreach loop to go through all the 
		/// HUDs when the order of the huds may be changed by the loop (such 
		/// as during WindowMessage processing).
		/// </summary>
		private List<IManagedHud> mHudsListCopy = new List<IManagedHud>();
		private bool mHudsListChanged = false;

		private PluginHost mHost;
		private CoreManager mCore;
		private MyClasses.MetaViewWrappers.IView mDefaultView;
		DefaultViewActiveDelegate mDefaultViewActive;
		private DecalTimer mRepaintHeartbeat;

		private ToolTipHud mToolTip;

		private Point mMousePos;

		private bool mDisposed = false;

		/// <summary>
		/// Occurs when this HudManager or any IManagedHud managed by this
		/// encounters an unhandled exception in one of its event handlers 
		/// (like WindowsMessage or GraphicsReset).
		/// </summary>
		public event EventHandler<ExceptionEventArgs> ExceptionHandler;

		/// <summary>Occurs once per frame.</summary>
		public event EventHandler Heartbeat;

		public event EventHandler<RegionChange3DEventArgs> RegionChange3D;

		/// <summary>
		/// Constructs a new instance of a HudManager. You <b>must</b> also register
		///  the GraphicsReset() function for the PluginBase.GraphicsReset event.
		/// </summary>
		/// <param name="host">PluginBase.Host</param>
		/// <param name="core">PluginBase.Core</param>
		/// <param name="startHeartbeatNow">If this is true, the heartbeat 
		///		timer will start ticking right away. This is generally 
		///		undesirable if this HudManager is created in the Startup() 
		///		method of the plugin. If this is false, you must call 
		///		<see cref="StartHeartbeat()"/> at a later time, such as during 
		///		the PlayerLogin event.</param>
		public HudManager(PluginHost host, CoreManager core, MyClasses.MetaViewWrappers.IView defaultView, DefaultViewActiveDelegate defaultViewActive, bool startHeartbeatNow)
		{
			mHost = host;
			mCore = core;
			mDefaultView = defaultView;
			mDefaultViewActive = defaultViewActive;

			mToolTip = new ToolTipHud(this);

			mRepaintHeartbeat = new DecalTimer();
			mRepaintHeartbeat.Timeout += new Decal.Interop.Input.ITimerEvents_TimeoutEventHandler(RepaintHeartbeatDispatch);
			if (startHeartbeatNow)
				StartHeartbeat();

			//Core.WindowMessage += new EventHandler<WindowMessageEventArgs>(WindowMessageDispatch);
		}

		/// <summary>
		/// Starts the heartbeat timer if it was not started when this 
		/// HudManager was created.
		/// </summary>
		public void StartHeartbeat()
		{
			if (!mRepaintHeartbeat.Running)
			{
				// The timeout is redicuously short, but Decal Timers only 
				// fire at most once per frame.
				mRepaintHeartbeat.Start(1);
			}
		}

		/// <summary>
		/// Cleans up the HudManager and Disposes all windows that are 
		/// being managed by this HudManager. Use this function when the 
		/// plugin is shutting down. Also be sure to unregister 
		/// <see cref="GraphicsReset()"/> from the GraphicsReset event.
		/// </summary>
		public void Dispose()
		{
			if (Disposed)
				return;

			if (mRepaintHeartbeat.Running)
				mRepaintHeartbeat.Stop();
			mRepaintHeartbeat.Timeout -= RepaintHeartbeatDispatch;
			mRepaintHeartbeat = null;

			//Core.WindowMessage -= WindowMessageDispatch;

			// Need to use a copy of the list because the Dispose() method of 
			// windows modifies mWindowList.
			UpdateHudsListCopy();
			foreach (IManagedHud hud in mHudsListCopy)
			{
				hud.Dispose();
			}
			mHudsOnTopList.Clear();
			mHudsList.Clear();
			mHudsListCopy.Clear();

			mHost = null;
			mCore = null;
			mDefaultView = null;
			mDefaultViewActive = null;

			mDisposed = true;
		}

		/// <summary>
		/// Gets whether this HudManager has been disposed.
		/// </summary>
		public bool Disposed
		{
			get { return mDisposed; }
		}

		/// <summary>
		/// Sets a Hud's always on top status. When a hud is always on top, it 
		/// will be painted above all other non-always-on-top huds.
		/// </summary>
		/// <param name="hud">The hud to set.</param>
		/// <param name="alwaysOnTop">Whether the given hud should be always on
		///		top of other huds.</param>
		public void SetAlwaysOnTop(IManagedHud hud, bool alwaysOnTop)
		{
			if (alwaysOnTop)
			{
				if (mHudsList.Remove(hud))
				{
					mHudsOnTopList.AddFirst(hud);
					hud.RecreateHud();
				}
			}
			else if (mHudsOnTopList.Remove(hud))
			{
				mHudsList.AddFirst(hud);
				hud.RecreateHud();
				RecreateInReverseOrder(mHudsOnTopList, false);
			}
		}

		/// <summary>
		/// Checks if a Hud is always on top of other huds.
		/// </summary>
		/// <param name="hud">The hud to check.</param>
		/// <returns>True if the specified hud is set to always be on top of 
		///		other huds. This function will return false if the hud is not 
		///		managed by this HudManager.</returns>
		public bool IsAlwaysOnTop(IManagedHud hud)
		{
			return mHudsOnTopList.Contains(hud);
		}

		/// <summary>
		/// Puts the specified hud on top of all other Huds. This function does 
		/// nothing if the hud is not managed by this manager or has been 
		/// disposed.
		/// </summary>
		/// <param name="hud">The hud to move to the top.</param>
		/// <param name="forceRecreateHud">Whether to force a recreate of the 
		///		given HUD, even if it is already at the front.</param>
		public void BringToFront(IManagedHud hud, bool forceRecreateHud)
		{
			// Check if the hud is already on top
			if (mHudsList.Count > 0 && mHudsList.First.Value == hud
					|| mHudsOnTopList.Count > 0 && mHudsOnTopList.First.Value == hud)
			{
				if (forceRecreateHud)
				{
					RecreateHud(hud);
				}
				return;
			}

			if (mHudsList.Remove(hud))
			{
				mHudsList.AddFirst(hud);
				hud.RecreateHud();
				// Recreate the AlwaysOnTop huds to keep them on top of this one
				RecreateInReverseOrder(mHudsOnTopList, false);
			}
			else if (mHudsOnTopList.Remove(hud))
			{
				mHudsOnTopList.AddFirst(hud);
				hud.RecreateHud();
			}

			mHudsListChanged = true;
		}

		/// <summary>
		/// Puts the specified hud behind all other Huds that are managed by 
		/// this HudManager. This function does nothing if the hud is not 
		/// managed by this manager or has been disposed.
		/// </summary>
		/// <remarks>
		/// This function has the side effect of moving all WindowHuds that 
		/// are managed by this HudManager above all other HUDs in Decal, 
		/// but only if the specified hud is not already behind all other 
		/// windows managed by this manager.
		/// </remarks>
		/// <param name="hud">The hud to move to the back.</param>
		/// <param name="forceRecreateHud">Whether to force a recreate of the 
		///		given HUD, even if it is already at the back.</param>
		public void SendToBack(IManagedHud hud, bool forceRecreateHud)
		{
			// Check if the hud is already at the back
			if (mHudsList.Count > 0 && mHudsList.Last.Value == hud
					|| mHudsOnTopList.Count > 0 && mHudsOnTopList.Last.Value == hud)
			{
				if (forceRecreateHud)
				{
					RecreateHud(hud);
				}
				return;
			}

			if (mHudsList.Remove(hud))
			{
				mHudsList.AddLast(hud);
				RecreateInReverseOrder(mHudsList, true);
				RecreateInReverseOrder(mHudsOnTopList, false);
			}
			else if (mHudsOnTopList.Remove(hud))
			{
				mHudsOnTopList.AddLast(hud);
				RecreateInReverseOrder(mHudsOnTopList, true);
			}

			mHudsListChanged = true;
		}

		/// <summary>
		/// Recreates the specified hud and any huds that are on top of it, to
		/// maintain Z-ordering. Use this function instead of calling the HUD's
		/// RecreateHud() function directly. This function does nothing if the 
		/// hud is not managed by this manager or has been disposed.
		/// </summary>
		/// <param name="hud">The HUD to recreate.</param>
		public void RecreateHud(IManagedHud hud)
		{
			LinkedListNode<IManagedHud> hudNode;
			if ((hudNode = mHudsList.Find(hud)) != null)
			{
				RecreateInReverseOrder(mHudsList, hudNode);
				RecreateInReverseOrder(mHudsOnTopList, false);
			}
			else if ((hudNode = mHudsOnTopList.Find(hud)) != null)
			{
				RecreateInReverseOrder(mHudsOnTopList, hudNode);
			}
		}

		/// <summary>
		/// Register this function to receive PluginBase.GraphicsReset events.
		/// </summary>
		public void GraphicsReset(object sender, EventArgs e)
		{
			try
			{
				if (!Disposed)
				{
					// Recreate the huds in reverse order to mainain z-ordering
					RecreateInReverseOrder(mHudsList, false);
					RecreateInReverseOrder(mHudsOnTopList, false);
				}
			}
			catch (Exception ex) { HandleException(ex); }
		}

		public PluginHost Host
		{
			get { return mHost; }
		}

		public CoreManager Core
		{
			get { return mCore; }
		}

		public MyClasses.MetaViewWrappers.IView DefaultView
		{
			get { return mDefaultView; }
		}

		public bool DefaultViewActive
		{
			get { return mDefaultViewActive(); }
		}

		/// <summary>
		/// Fires the ExceptionHandler event. Huds should call this function 
		/// in the event of an unhandled exception that is in event handling 
		/// code (such as a timer's tick).
		/// </summary>
		/// <param name="ex">The exception that occurred.</param>
		public void HandleException(Exception ex)
		{
			if (ExceptionHandler != null)
				ExceptionHandler(null, new ExceptionEventArgs(ex));
		}

		/// <summary>
		/// Adds a new hud to the HudManager, on top of all other huds. If the
		/// Hud has been registered with another HudManager, it will be 
		/// unregistered from that manager. The hud will receive GraphicsReset, 
		/// WindowMessage, and RepaintHeartbeat events.
		/// <para>Calls RecreateHud() on the given hud to make sure that it 
		/// is on top.</para>
		/// </summary>
		/// <param name="hud">The hud to add.</param>
		/// <param name="alwaysOnTop">Indicates if the hud is always on top of 
		///		other huds.</param>
		public void RegisterHud(IManagedHud hud, bool alwaysOnTop)
		{
			if (hud.Manager != null)
			{
				hud.Manager.UnregisterHud(hud);
			}
			if (alwaysOnTop)
			{
				mHudsOnTopList.AddFirst(hud);
				hud.RecreateHud();
			}
			else
			{
				mHudsList.AddFirst(hud);
				hud.RecreateHud();
				RecreateInReverseOrder(mHudsOnTopList, false);
			}
			mHudsListChanged = true;
		}

		/// <summary>
		/// Removes a hud from the HudManager. It will no longer receive
		/// GraphicsReset, WindowMessage, or RepaintHeartbeat events.
		/// </summary>
		/// <param name="hud">The hud to remove.</param>
		public void UnregisterHud(IManagedHud hud)
		{
			mHudsList.Remove(hud);
			mHudsOnTopList.Remove(hud);
			mHudsListChanged = true;
		}

		/// <summary>
		/// Checks if the mouse is hovering on this hud and NOT hovering on 
		/// any hud that's on top of the specified hud.
		/// </summary>
		/// <remarks>
		/// This will usually be called during WindowMessageDispatch by a 
		/// hud during a WM_MOUSEMOVE event. Thus, not all huds will have 
		/// been notified that the mouse has moved yet. This is okay because 
		/// all huds on top of this hud WILL have been notified since 
		/// they're notified in Z-order, and the huds that are on top are 
		/// the only huds that matter.
		/// </remarks>
		/// <param name="hudToCheck">The hud to check.</param>
		/// <returns>True if the mouse is hovering on the specified hud and 
		/// NOT hovering on a hud that's on top of the hud.</returns>
		public bool MouseHoveringOnHud(IManagedHud hudToCheck)
		{
			//UpdateHudsListCopy();
			if (DefaultViewActive && DefaultView.Position.Contains(mMousePos))
			{
				return false;
			}
			foreach (IManagedHud hud in mHudsListCopy)
			{
				if (hud == hudToCheck)
				{
					return hud.MouseHoveringObscuresOther;
				}
				else if (hud.MouseHoveringObscuresOther)
				{
					return false;
				}
			}
			return false;
		}

		public void ShowToolTip(Point location, string message)
		{
			mToolTip.Show(location, message);
		}

		public void ShowToolTip(Point location, string message, int hideDelayMillis)
		{
			mToolTip.Show(location, message, hideDelayMillis);
		}

		public void HideToolTip()
		{
			mToolTip.Hide();
		}

		private void UpdateHudsListCopy()
		{
			if (mHudsListChanged)
			{
				mHudsListCopy.Clear();
				mHudsListCopy.AddRange(mHudsOnTopList);
				mHudsListCopy.AddRange(mHudsList);
				mHudsListChanged = false;
			}
		}

		private void RecreateInReverseOrder(LinkedList<IManagedHud> hudsList, bool skipLast)
		{
			if (hudsList.Count > 0)
			{
				RecreateInReverseOrder(hudsList, skipLast ? hudsList.Last.Previous : hudsList.Last);
			}
		}

		private void RecreateInReverseOrder(LinkedList<IManagedHud> hudsList, LinkedListNode<IManagedHud> start)
		{
			for (LinkedListNode<IManagedHud> i = start; i != null; i = i.Previous)
			{
				i.Value.RecreateHud();
			}
		}

		public void DispatchRegionChange3D(object sender, RegionChange3DEventArgs e)
		{
			try
			{
				// Forward the event
				if (RegionChange3D != null) { RegionChange3D(sender, e); }
			}
			catch (Exception ex) { HandleException(ex); }
		}

		public void DispatchWindowMessage(object sender, WindowMessageEventArgs e)
		{
			try
			{
				if (e.Msg >= WM_MOUSEFIRST && e.Msg <= WM_MOUSELAST && !Disposed)
				{
					if (e.Msg == WM_MOUSEMOVE)
					{
						mMousePos = new Point(e.LParam);
					}
					// Don't handle mouse events when the mouse is on the view
					else if (DefaultViewActive &&
						(e.Msg == WM_LBUTTONDOWN || e.Msg == WM_MBUTTONDOWN ||
						 e.Msg == WM_RBUTTONDOWN || e.Msg == WM_MOUSEWHEEL)
						&& DefaultView.Position.Contains(new Point(e.LParam)))
					{
						return;
					}

					// Make a copy of the list in case it is modified while 
					// processing the message (like if one of the mouse event
					// handlers calls BringToFront()).
					UpdateHudsListCopy();
					bool origEat = e.Eat;
					if (!e.Eat)
					{
						foreach (IManagedHud hud in mHudsListCopy)
						{
							// It's possible for the hud to be disposed if a 
							// mouse event handler for another one disposes it.
							if (!hud.Disposed)
							{
								hud.WindowMessage(e);
								if (e.Eat)
									break;
							}
						}

						// Don't let huds eat mouse moves
						if (e.Eat != origEat && e.Msg == WM_MOUSEMOVE)
						{
							e.Eat = origEat;
						}
					}
				}
			}
			catch (Exception ex) { HandleException(ex); }
		}

		/// <summary>
		/// This is called once per frame by the mRepaintHeartbeat timer.
		/// It just tells each of the huds to repaint if they need to.
		/// </summary>
		private void RepaintHeartbeatDispatch(Decal.Interop.Input.Timer Source)
		{
			try
			{
				if (!Disposed)
				{
					if (Heartbeat != null)
						Heartbeat(this, EventArgs.Empty);

					// Make a copy of the list in case it is modified while 
					// repainting. As of writing this comment that won't happen, 
					// but it's a quick check...
					UpdateHudsListCopy();
					foreach (IManagedHud hud in mHudsListCopy)
					{
						hud.RepaintHeartbeat();
					}
				}
			}
			catch (Exception ex) { HandleException(ex); }
		}
	}
}
