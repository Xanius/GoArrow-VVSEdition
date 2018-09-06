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
using System.Drawing;
using System.Xml;

namespace GoArrow.RouteFinding
{
	public class Route : List<Location>
	{
		private double mDistance = 0;

		public Route(double distance)
		{
			mDistance = distance;
		}

		public double Distance
		{
			get { return mDistance; }
			//set { mDistance = value; }
		}

		public XmlElement ToXml(XmlDocument ownerDoc)
		{
			XmlElement ele = ownerDoc.CreateElement("route");
			ele.SetAttribute("distance", mDistance.ToString());
			foreach (Location loc in this)
			{
				XmlElement locNode;
				if (loc.IsInternalLocation)
				{
					locNode = loc.ToXml(ownerDoc);
					locNode.InnerText = "";
				}
				else
				{
					locNode = (XmlElement)ele.AppendChild(ownerDoc.CreateElement("loc"));
					locNode.SetAttribute("id", loc.Id.ToString());
					locNode.SetAttribute("name", loc.Name);
				}
				ele.AppendChild(locNode);
			}
			return ele;
		}

		public static Route FromXml(XmlElement ele, LocationDatabase locDb)
		{
			Route r = new Route(double.Parse(ele.GetAttribute("distance")));
			foreach (XmlElement locNode in ele.GetElementsByTagName("loc"))
			{
				Location loc;
				if (!locDb.TryGet(int.Parse(locNode.GetAttribute("id")), out loc))
				{
					loc = Location.FromXml(locNode, true);
				}

				r.Add(loc);
			}
			return r;
		}
	}

	public class RouteFinderPackage
	{
		internal List<RouteFinder.Vertex> startVertices;
		internal Dictionary<int, RouteFinder.Vertex> entranceVertices;
		internal Dictionary<int, RouteFinder.Vertex> exitVertices;
	}

	public static class RouteFinder
	{
		private static double mPortalWeight = 0;
		private static double mMaxRunDistance = 50;

		/// <summary>The edge weight of a Portal Entrance -> Exit</summary>
		public static double PortalWeight
		{
			get { return mPortalWeight; }
			set { mPortalWeight = value; }
		}

		/// <summary>The maximum number of clicks from one portal to the next</summary>
		public static double MaxRunDistance
		{
			get { return mMaxRunDistance; }
			set { mMaxRunDistance = value; }
		}

		#region Region Handling
		private class Polygon
		{
			private PointF[] mPoints;
			private RectangleF mBoundingRect = Rectangle.Empty;

			public Polygon(PointF[] points)
			{
				mPoints = points;
			}

			public Polygon(int numPoints) : this(new PointF[numPoints]) { }

			public PointF this[int i]
			{
				get { return mPoints[i]; }
				set
				{
					mPoints[i] = value;
					mBoundingRect = Rectangle.Empty;
				}
			}

			public bool Contains(Coordinates coords)
			{
				return Contains((float)coords.EW, (float)coords.NS);
			}

			public bool Contains(PointF pt)
			{
				return Contains(pt.X, pt.Y);
			}

			public bool Contains(float x, float y)
			{
				if (mBoundingRect.IsEmpty)
				{
					CalcBoundingRect();
				}

				if (!mBoundingRect.Contains(x, y))
				{
					return false;
				}

				bool inPoly = false;
				for (int i = 0, j = mPoints.Length - 1; i < mPoints.Length; j = i++)
				{
					float iX = mPoints[i].X, iY = mPoints[i].Y;
					float jX = mPoints[j].X, jY = mPoints[j].Y;

					if (((iY <= y && y < jY) || (jY <= y && y < iY)) &&
						(x < (jX - iX) * (y - iY) / (jY - iY) + iX))
					{
						inPoly = !inPoly;
					}
				}
				return inPoly;
			}

			private void CalcBoundingRect()
			{
				if (mPoints.Length > 0)
				{
					float minX, maxX, minY, maxY;
					minX = maxX = mPoints[0].X;
					minY = maxY = mPoints[0].Y;
					for (int i = 1; i < mPoints.Length; i++)
					{
						PointF p = mPoints[i];
						if (p.X < minX)
							minX = p.X;
						else if (p.X > maxX)
							maxX = p.X;
						if (p.Y < minY)
							minY = p.Y;
						else if (p.Y > maxY)
							maxY = p.Y;
					}
					mBoundingRect = RectangleF.FromLTRB(minX, minY, maxX, maxY);
				}
				else
				{
					mBoundingRect = Rectangle.Empty;
				}
			}

			public PointF[] Points
			{
				get { return mPoints; }
			}

			public PointF[] GetPointsCopy()
			{
				PointF[] points = new PointF[mPoints.Length];
				Array.Copy(mPoints, points, mPoints.Length);
				return points;
			}

			public PointF[] GetPointsCopy(System.Drawing.Drawing2D.Matrix transform)
			{
				PointF[] points = new PointF[mPoints.Length];
				Array.Copy(mPoints, points, mPoints.Length);
				transform.TransformPoints(points);
				return points;
			}
		}

		/// <summary>
		/// Draws the map regions to the graphics surface. The surface must 
		/// have a transform applied to it to translate the Dereth coordinate 
		/// space to the image's pixel coordinate space.
		/// </summary>
		public static void DrawRegions(Graphics g, Brush regionFill, Brush innerRegionFill)
		{
			LazyLoadRegions();
			System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
			System.Drawing.Drawing2D.GraphicsPath innerPath = new System.Drawing.Drawing2D.GraphicsPath();

			foreach (Polygon poly in mPolygonRegions) { path.AddPolygon(poly.Points); }
			if (mRectangleRegions.Length > 0) { path.AddRectangles(mRectangleRegions); }

			foreach (Polygon innerPoly in mInnerPolygonRegions) { innerPath.AddPolygon(innerPoly.Points); }
			if (mInnerRectangleRegions.Length > 0) { innerPath.AddRectangles(mInnerRectangleRegions); }

			Region region = new Region(path);
			Region innerRegion = new Region(innerPath);
			region.Exclude(innerRegion);

			g.FillRegion(regionFill, region);
			g.FillRegion(innerRegionFill, innerRegion);
		}

		private static Polygon[] mPolygonRegions;
		private static Polygon[] mInnerPolygonRegions;
		private static RectangleF[] mRectangleRegions;
		private static RectangleF[] mInnerRectangleRegions;

		private static void LazyLoadRegions()
		{
			if (mPolygonRegions == null)
			{
				XmlDocument mapRegions = new XmlDocument();

				// Load from file if file exists
				// If error or file doesn't exist, load from resource
				string path = Util.FullPath("MapRegions.xml");
				if (System.IO.File.Exists(path))
				{
					try { mapRegions.Load(path); }
					catch (Exception ex)
					{
						Util.HandleException(ex, "Error occurred while loading MapRegions.xml", false);
						mapRegions.LoadXml(RouteFinding.Data.MapRegions);
					}
				}
				else { mapRegions.LoadXml(RouteFinding.Data.MapRegions); }

				XmlNodeList polys = mapRegions.DocumentElement.GetElementsByTagName("poly");
				mPolygonRegions = new Polygon[polys.Count];
				for (int i = 0; i < polys.Count; i++)
				{
					XmlNodeList points = ((XmlElement)polys[i]).GetElementsByTagName("coords");
					mPolygonRegions[i] = new Polygon(points.Count);
					for (int j = 0; j < points.Count; j++)
					{
						float NS = float.Parse(((XmlElement)points[j]).GetAttribute("NS"));
						float EW = float.Parse(((XmlElement)points[j]).GetAttribute("EW"));
						mPolygonRegions[i][j] = new PointF(EW, NS);
					}
				}

				polys = mapRegions.DocumentElement.GetElementsByTagName("innerPoly");
				mInnerPolygonRegions = new Polygon[polys.Count];
				for (int i = 0; i < polys.Count; i++)
				{
					XmlNodeList points = ((XmlElement)polys[i]).GetElementsByTagName("coords");
					mInnerPolygonRegions[i] = new Polygon(points.Count);
					for (int j = 0; j < points.Count; j++)
					{
						float NS = float.Parse(((XmlElement)points[j]).GetAttribute("NS"));
						float EW = float.Parse(((XmlElement)points[j]).GetAttribute("EW"));
						mInnerPolygonRegions[i][j] = new PointF(EW, NS);
					}
				}

				XmlNodeList rects = mapRegions.DocumentElement.GetElementsByTagName("rect");
				mRectangleRegions = new RectangleF[rects.Count];
				for (int i = 0; i < rects.Count; i++)
				{
					XmlNodeList points = ((XmlElement)rects[i]).GetElementsByTagName("coords");
					if (points.Count == 2)
					{
						float NS1 = float.Parse(((XmlElement)points[0]).GetAttribute("NS"));
						float EW1 = float.Parse(((XmlElement)points[0]).GetAttribute("EW"));
						float NS2 = float.Parse(((XmlElement)points[1]).GetAttribute("NS"));
						float EW2 = float.Parse(((XmlElement)points[1]).GetAttribute("EW"));
						mRectangleRegions[i] = RectangleF.FromLTRB(
							Math.Min(EW1, EW2), Math.Min(NS1, NS2),
							Math.Max(EW1, EW2), Math.Max(NS1, NS2));
					}
				}

				rects = mapRegions.DocumentElement.GetElementsByTagName("innerRect");
				mInnerRectangleRegions = new RectangleF[rects.Count];
				for (int i = 0; i < rects.Count; i++)
				{
					XmlNodeList points = ((XmlElement)rects[i]).GetElementsByTagName("coords");
					if (points.Count == 2)
					{
						float NS1 = float.Parse(((XmlElement)points[0]).GetAttribute("NS"));
						float EW1 = float.Parse(((XmlElement)points[0]).GetAttribute("EW"));
						float NS2 = float.Parse(((XmlElement)points[1]).GetAttribute("NS"));
						float EW2 = float.Parse(((XmlElement)points[1]).GetAttribute("EW"));
						mInnerRectangleRegions[i] = RectangleF.FromLTRB(
							Math.Min(EW1, EW2), Math.Min(NS1, NS2),
							Math.Max(EW1, EW2), Math.Max(NS1, NS2));
					}
				}
			}
		}

		private static int GetRegionId(Coordinates coords)
		{
			float x = (float)coords.EW;
			float y = (float)coords.NS;

			int region = 1;
			foreach (RectangleF rect in mInnerRectangleRegions)
			{
				if (rect.Contains(x, y))
					return region;
				region++;
			}
			foreach (Polygon poly in mInnerPolygonRegions)
			{
				if (poly.Contains(x, y))
					return region;
				region++;
			}
			foreach (RectangleF rect in mRectangleRegions)
			{
				if (rect.Contains(x, y))
					return region;
				region++;
			}
			foreach (Polygon poly in mPolygonRegions)
			{
				if (poly.Contains(x, y))
					return region;
				region++;
			}

			// If the location is not in any region, use default of 0 (mainland)
			return 0;
		}
		#endregion

		#region Inner Classes
		internal enum VertexCategory { Entrance, Exit }

		internal class Vertex
		{
			public Location Loc;
			public readonly VertexCategory Category;
			public readonly int RegionId; // Mainland is region 0
			public double ShortestPathCost = double.PositiveInfinity; // d[v]
			public Vertex Next = null;                                // next[v]
			public List<Edge> IncomingEdges = new List<Edge>();
#if ENABLE_FORWARD_ALGORITHM
			public Vertex Previous = null;                            // previous[v]
			public List<Edge> OutgoingEdges = new List<Edge>();
#endif

			public Vertex(Location l, VertexCategory cat)
			{
				Loc = l;
				Category = cat;
				if (cat == VertexCategory.Exit && l.HasExitCoords)
				{
					RegionId = GetRegionId(l.ExitCoords);
				}
				else
				{
					RegionId = GetRegionId(l.Coords);
				}
			}

			public override string ToString()
			{
				string pathCost;
				if (double.IsPositiveInfinity(ShortestPathCost))
					pathCost = "\u221E";
				else if (double.IsNegativeInfinity(ShortestPathCost))
					pathCost = "-\u221E";
				else
					pathCost = ShortestPathCost.ToString("0.0");
				return Loc + "[" + Category + "](" + pathCost + ")";
			}

			public override bool Equals(object obj)
			{
				if (obj is Vertex)
				{
					Vertex v = (Vertex)obj;
					return this == v;
				}
				return false;
			}

			public override int GetHashCode()
			{
				return Loc.GetHashCode() ^ (int)Category;
			}

			public static bool operator ==(Vertex a, Vertex b)
			{
				if ((object)a == null || (object)b == null)
					return (object)a == null && (object)b == null;
				return a.Loc == b.Loc && a.Category == b.Category;
			}
			public static bool operator !=(Vertex a, Vertex b) { return !(a == b); }

			public static readonly Vertex NULL_VERTEX = new Vertex(Location.NO_LOCATION,
				VertexCategory.Entrance);
		}

		internal class Edge
		{
			public Vertex From;   // u
			public Vertex To;     // v
			public double Weight; // w(u,v)

			public static List<Edge> All = new List<Edge>();

			public Edge(Vertex from, Vertex to, double weight)
			{
				this.From = from;
				this.To = to;
				this.Weight = weight;
				All.Add(this);
			}

			public override string ToString()
			{
				return From + " -> " + To + " || " + Weight.ToString("0.0");
			}
		}
		#endregion

		/// <summary>
		/// Finds the shortest route from any number of start locations to a specific end location
		/// </summary>
		/// <param name="startPoints">The list of the alternate start points</param>
		/// <param name="startLocation">The specific start point specified by the user, or 
		///		NO_LOCATION if the algorithm should only use the locations in startPoints.</param>
		/// <param name="endLocation">The route destination</param>
		/// <param name="p">This will describe the graph generated for route finding. 
		///		Pass it as the parameter to FindRoute(RouteFinderPackage) to find the 
		///		next-longer route.</param>
		/// <returns>The shortest route from endLocation to any of the start points.</returns>
		public static Route FindRoute(LocationDatabase locDb, ICollection<RouteStart> startPoints,
				ICollection<PortalDevice> portalDevices, Location startLocation, Location endLocation,
				out RouteFinderPackage p)
		{

			// Implementation Notes:
			//   - Based on Dijkstra's Algorithm
			//   - Maintains two "Q" lists, one for portal entrances and one for portal exits.
			//   - "Running" edges exist from portal exits to portal entrances, but only if 
			//	   the two points are in the same region (i.e., island).
			//   - "Portal" edges exist from portal entrances to portal exits.
			//   - Finds the route backwards, starting at the end location and working until 
			//     it gets to any of the start locations
			//
			// Reference: http://en.wikipedia.org/wiki/Dijkstra's_algorithm
			// 1   function Dijkstra(G, w, s)
			// 2      for each vertex v in V[G]                       // Initializations
			// 3            d[v] := infinity
			// 4            previous[v] := undefined
			// 5      d[s] := 0
			// 6      S := empty set
			// 7      Q := set of all vertices
			// 8      while Q is not an empty set
			// 9            u := Extract_Min(Q)
			// 10           S := S union {u}
			// 11           for each edge (u,v) outgoing from u
			// 12                 if d[v] > d[u] + w(u,v)             // Relax (u,v)
			// 13                       d[v] := d[u] + w(u,v)
			// 14                       previous[v] := u

			LazyLoadRegions();
			p = new RouteFinderPackage();

			// Estimate the number of vertices..
			int numLocations = locDb.PortalLocations.Count + startPoints.Count + portalDevices.Count + 4;

			// The vertices representing portal entrances
			p.entranceVertices = new Dictionary<int, Vertex>(numLocations);
			// The vertices representing portal exits
			p.exitVertices = new Dictionary<int, Vertex>(numLocations);

			// (7) Q := set of all vertices
			// (2-4) Done in Vertex constructors
			foreach (Location portal in locDb.PortalLocations)
			{
				if (portal.UseInRouteFinding)
				{
					Vertex entrance, exit;
					p.entranceVertices[portal.Id] = entrance = new Vertex(portal, VertexCategory.Entrance);
					p.exitVertices[portal.Id] = exit = new Vertex(portal, VertexCategory.Exit);

					double portalWeight = mPortalWeight;
					if (portal.Type == LocationType.UndergroundPortal)
						portalWeight *= 2;

					Edge edge = new Edge(entrance, exit, portalWeight);

					//entrance.OutgoingEdges.Add(edge);
					exit.IncomingEdges.Add(edge);
				}
			}

			// Add portal devices
			foreach (PortalDevice device in portalDevices)
			{
				if (device.Detected && device.Enabled)
				{
					foreach (Location dest in device.Destinations)
					{
						Vertex entrance, exit;
						p.entranceVertices[dest.Id] = entrance = new Vertex(dest, VertexCategory.Entrance);
						p.exitVertices[dest.Id] = exit = new Vertex(dest, VertexCategory.Exit);

						Edge edge = new Edge(entrance, exit, device.RunDistance);

						//entrance.OutgoingEdges.Add(edge);
						exit.IncomingEdges.Add(edge);
					}
				}
			}

			// Add vertices for start and end if they aren't already in the list.
			p.startVertices = new List<Vertex>(startPoints.Count);
			Dictionary<Vertex, double> startVerticesExtraRun = new Dictionary<Vertex, double>();
			Vertex end;

			// startVerticies are like portal exits - make sure they're in the 
			// exit list, and not in the entrance list
			Vertex start;
			foreach (RouteStart startPoint in startPoints)
			{
				if (startPoint.Enabled && startPoint.Coords != Coordinates.NO_COORDINATES)
				{
					if (!p.exitVertices.TryGetValue(startPoint.Id, out start))
					{
						start = new Vertex(startPoint.ToLocation(locDb), VertexCategory.Exit);
						p.exitVertices[startPoint.Id] = start;
					}
					p.entranceVertices.Remove(startPoint.Id);
					start.IncomingEdges.Clear();

					if (start.Loc.ExitCoords == Coordinates.NO_COORDINATES)
						start.Loc.ExitCoords = start.Loc.Coords;

					p.startVertices.Add(start);
					if (startPoint.RunDistance > 0)
						startVerticesExtraRun[start] = startPoint.RunDistance;
				}
			}
			if (startLocation != null && startLocation != Location.NO_LOCATION)
			{
				if (!p.exitVertices.TryGetValue(startLocation.Id, out start))
				{
					start = new Vertex(startLocation, VertexCategory.Exit);
					p.exitVertices[startLocation.Id] = start;
				}
				start.IncomingEdges.Clear();
				p.entranceVertices.Remove(startLocation.Id);

				if (start.Loc.ExitCoords == Coordinates.NO_COORDINATES)
				{
					// Create a copy so as to not modify the original Location
					start.Loc = new Location(start.Loc.Id, start.Loc);
					start.Loc.ExitCoords = start.Loc.Coords;
				}

				p.startVertices.Add(start);
			}

			// end is like a portal entrance - make sure it's in the 
			// entrance list, and not in the exit list
			if (!p.entranceVertices.TryGetValue(endLocation.Id, out end))
			{
				end = new Vertex(endLocation, VertexCategory.Entrance);
				p.entranceVertices[endLocation.Id] = end;
			}
			p.exitVertices.Remove(endLocation.Id);
			//end.OutgoingEdges.Clear();

			// (5) d[s] := 0
			end.ShortestPathCost = 0;

			// Add edges for running from each portal exit to each portal entrance
			// ONLY add edges if the two vertices have the same region ID (i.e., are
			// on the same island).
			foreach (Vertex exit in p.exitVertices.Values)
			{
				foreach (Vertex entrance in p.entranceVertices.Values)
				{
					if (entrance.Loc != exit.Loc && entrance.RegionId == exit.RegionId)
					{
						double distance = exit.Loc.ExitCoords.DistanceTo(entrance.Loc.Coords);
						double extraRun;
						if (startVerticesExtraRun.TryGetValue(exit, out extraRun))
							distance += extraRun;

						if (distance < mMaxRunDistance)
						{
							Edge e = new Edge(exit, entrance, distance);
							//exit.OutgoingEdges.Add(e);
							entrance.IncomingEdges.Add(e);
						}
					}
				}
			}

			return FindRoute(p);
		}

		/// <summary>
		/// Finds the shortest route in the graph described by the RouteFinderPackage p.
		/// Modifies p so that the next time it is used to find a route, the next-longer
		/// route will be found.
		/// </summary>
		/// <param name="p">The RouteFinderPackage describing the graph on which to find 
		///		the route.</param>
		/// <returns>The shortest route in the graph described by p.</returns>
		public static Route FindRoute(RouteFinderPackage p)
		{
			Vertex startVertex = null;

			// (8) while Q is not an empty set
			while (p.entranceVertices.Count > 0 || p.exitVertices.Count > 0)
			{
				// (9) u := Extract_Min(Q)
				Vertex u = Vertex.NULL_VERTEX;
				bool isEntranceVertex = true;
				foreach (Vertex v in p.entranceVertices.Values)
				{
					if (v.ShortestPathCost <= u.ShortestPathCost)
					{
						u = v;
					}
				}
				foreach (Vertex v in p.exitVertices.Values)
				{
					if (v.ShortestPathCost <= u.ShortestPathCost)
					{
						u = v;
						isEntranceVertex = false;
					}
				}
				if (isEntranceVertex)
					p.entranceVertices.Remove(u.Loc.Id);
				else
					p.exitVertices.Remove(u.Loc.Id);

				// If we're at one of the start locations, we've found a path
				if (p.startVertices.Contains(u))
				{
					startVertex = u;
					//p.startVertices.Remove(u);
					break;
				}

				// (11) for each edge (u,v) outgoing from u
				// Note: This search is being done backwards, so look at incoming edges instead
				foreach (Edge e in u.IncomingEdges)
				{
					Vertex v = e.From;
					// (12) if d[v] > d[u] + w(u,v) // Relax (u,v)
					if (v.ShortestPathCost > u.ShortestPathCost + e.Weight)
					{
						// (13) d[v] := d[u] + w(u,v)
						v.ShortestPathCost = u.ShortestPathCost + e.Weight;
						// (14) previous[v] := u
						v.Next = u;
					}
				}
			}

			// Read the route back
			if (startVertex != null && !double.IsInfinity(startVertex.ShortestPathCost))
			{
				Route route = new Route(startVertex.ShortestPathCost);
				route.Add(startVertex.Loc);
				for (Vertex u = startVertex; u != null; u = u.Next)
				{
					if (u.Category == VertexCategory.Entrance)
						route.Add(u.Loc);
				}
				return route;
			}
			else
			{
				return new Route(0.0);
			}
		}
	}
}
