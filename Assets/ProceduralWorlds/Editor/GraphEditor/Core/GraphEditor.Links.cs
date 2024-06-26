﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Profiling;
using ProceduralWorlds.Core;
using ProceduralWorlds;

namespace ProceduralWorlds.Editor
{
	public partial class BaseGraphEditor
	{
		//process link creation, drag and select events + draw links
		void RenderLinks()
		{
			Profiler.BeginSample("[PW] render links");
	
			//render the dragged link
			if (editorEvents.isDraggingLink || editorEvents.isDraggingNewLink)
				DrawNodeCurve(editorEvents.startedLinkAnchor, e.mousePosition);
	
			//render node links
			foreach (var node in graph.allNodes)
				RenderNodeLinks(node);
			
			if (!editorEvents.isMouseOverLinkFrame)
				editorEvents.mouseOverLink = null;
	
			Profiler.EndSample();
		}
	
		void RenderNodeLinks(BaseNode node)
		{
			Handles.BeginGUI();
			foreach (var anchorField in node.outputAnchorFields)
				foreach (var anchor in anchorField.anchors)
					foreach (var link in anchor.links)
						DrawNodeCurve(link);
			Handles.EndGUI();
		}
		
		void DrawNodeCurve(Anchor anchor, Vector2 endPoint, bool anchorSnapping = true)
		{
			Rect anchorRect = anchor.rectInGraph;
			Vector3 startPos = new Vector3(anchorRect.x + anchorRect.width, anchorRect.y + anchorRect.height / 2, 0);
			Vector3 startDir = Vector3.right;
	
			if (anchorSnapping && editorEvents.isMouseOverAnchor)
			{
				var toAnchor = editorEvents.mouseOverAnchor;
				var fromAnchor = editorEvents.startedLinkAnchor;
	
				if (AnchorUtils.AnchorAreAssignable(fromAnchor, toAnchor))
					endPoint = toAnchor.rectInGraph.center;
			}
	
			float tanPower = (startPos - (Vector3)endPoint).magnitude / 2;
			tanPower = Mathf.Clamp(tanPower, 0, 100);
	
			DrawSelectedBezier(startPos, endPoint, startPos + startDir * tanPower, (Vector3)endPoint + -startDir * tanPower, anchor.colorSchemeName, 4, LinkHighlightMode.None);
		}
		
		void DrawNodeCurve(NodeLink link)
		{
			if (link == null)
			{
				Debug.LogError("[BaseGraphEditor] attempt to draw null link !");
				return ;
			}
			if (link.fromAnchor == null || link.toAnchor == null)
			{
				Debug.LogError("[BaseGraphEditor] null anchors in a the link: " + link);
				return ;
			}
	
			Event e = Event.current;
	
			link.controlId = GUIUtility.GetControlID(FocusType.Passive);
	
			Rect start = link.fromAnchor.rectInGraph;
			Rect end = link.toAnchor.rectInGraph;
	
			Vector3 startPos = new Vector3(start.x + start.width, start.y + start.height / 2, 0);
			Vector3 endPos = new Vector3(end.x, end.y + end.height / 2, 0);
	
			Vector3 startDir = Vector3.right;
			Vector3 endDir = Vector3.left;
			
			float tanPower = (startPos - endPos).magnitude / 2;
			tanPower = Mathf.Clamp(tanPower, 0, 100);
	
			Vector3 startTan = startPos + startDir * tanPower;
			Vector3 endTan = endPos + endDir * tanPower;
	
			if (e.type == EventType.MouseDown && !editorEvents.isMouseOverAnchor)
			{
				if (HandleUtility.nearestControl == link.controlId && e.button == 0)
				{
					GUIUtility.hotControl = link.controlId;
	
					NodeLink oldSelectedLink = graph.nodeLinkTable.GetLinks().FirstOrDefault(l => l.selected);
	
					if (oldSelectedLink != null && OnLinkUnselected != null)
						OnLinkUnselected(oldSelectedLink);
	
					//unselect all others links:
					UnselectAllLinks();
					UnselectAllNodes();
	
					link.selected = true;
					link.highlight = LinkHighlightMode.Selected;
	
					if (OnLinkSelected != null)
						OnLinkSelected(link);
					
					e.Use();
				}
			}
	
			//mouse over bezier curve:
			if (HandleUtility.nearestControl == link.controlId)
			{
				editorEvents.mouseOverLink = link;
				editorEvents.isMouseOverLinkFrame = true;
			}
	
			if (e.type == EventType.Repaint)
			{
				DrawSelectedBezier(startPos, endPos, startTan, endTan, link.colorSchemeName, 4, link.highlight);
	
				if (link.highlight == LinkHighlightMode.DeleteAndReset)
					link.highlight = LinkHighlightMode.None;
				
				if (!link.selected && link.highlight == LinkHighlightMode.Selected)
					link.highlight = LinkHighlightMode.None;
			}
			else if (e.type == EventType.Layout)
			{
				float bezierDistance = HandleUtility.DistancePointBezier(e.mousePosition, startPos, endPos, startTan, endTan);
				HandleUtility.AddControl(link.controlId, bezierDistance);
			}
		}
	
		void	DrawSelectedBezier(Vector3 startPos, Vector3 endPos, Vector3 startTan, Vector3 endTan, ColorSchemeName colorSchemeName, int width, LinkHighlightMode highlight)
		{
			switch (highlight)
			{
				case LinkHighlightMode.Selected:
					Handles.DrawBezier(startPos, endPos, startTan, endTan, ColorTheme.selectedColor, null, width + 3);
						break ;
				case LinkHighlightMode.Delete:
				case LinkHighlightMode.DeleteAndReset:
					Handles.DrawBezier(startPos, endPos, startTan, endTan, ColorTheme.deletedColor, null, width + 2);
					break ;
				default:
					break ;
			}
			Color c = ColorTheme.GetLinkColor(colorSchemeName);
			Handles.DrawBezier(startPos, endPos, startTan, endTan, c, null, width);
		}
	
		void PreLinkCreatedCallback()
		{
			const string undoName = "Link created";
	
			Undo.RecordObject(graph, undoName);
			Undo.RecordObject(editorEvents.startedLinkAnchor.nodeRef, undoName);
			Undo.RecordObject(editorEvents.mouseOverNode, undoName);
		}
	
		void LinkRemovedCallback(NodeLink link)
		{
			const string undoName = "Link removed";
			
			Undo.RecordObject(graph, undoName);
			Undo.RecordObject(link.fromNode, undoName);
			Undo.RecordObject(link.toNode, undoName);
		}
	
		void	LinkCreatedCallback(NodeLink link)
		{
			if (editorEvents.isDraggingLink || editorEvents.isDraggingNewLink)
				StopDragLink(true);
		}
	
		void	UnselectAllLinks()
		{
			foreach (var l in graph.nodeLinkTable.GetLinks())
			{
				l.selected = false;
				l.highlight = LinkHighlightMode.None;
			}
		}
		
		void StartDragLink()
		{
			graph.editorEvents.startedLinkAnchor = editorEvents.mouseOverAnchor;
			graph.editorEvents.isDraggingLink = true;
			
			if (OnLinkStartDragged != null)
				OnLinkStartDragged(editorEvents.startedLinkAnchor);
		}
	
		void StopDragLink(bool linked)
		{
			graph.editorEvents.isDraggingLink = false;
	
			if (!linked && OnLinkCanceled != null)
				OnLinkCanceled();
			
			if (OnLinkStopDragged != null)
				OnLinkStopDragged();
		}
	
		void DeleteAllAnchorLinks()
		{
			editorEvents.mouseOverAnchor.RemoveAllLinks();
		}
	}
}