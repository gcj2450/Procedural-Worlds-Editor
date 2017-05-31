﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using System.Linq;
using System;
using Debug = UnityEngine.Debug;

namespace PW.Core
{
	using Node;
	
	public enum	PWTerrainOutputMode
	{
		None,
		SideView2D,
		TopDown2D,
		Planar3D,
		Spherical3D,
		Cubic3D,
		Density1D,
		Density2D,
		Density3D,
		Mesh,
	}

	[CreateAssetMenu(fileName = "New ProceduralWorld", menuName = "Procedural World", order = 1)]
	[System.SerializableAttribute]
	public class PWNodeGraph : ScriptableObject {
	
		[SerializeField]
		public int							majorVersion = 0;
		public int							minorVersion = 0;

		[SerializeField]
		public List< PWNode >				nodes = new List< PWNode >();
		[SerializeField]
		public List< PWOrderingGroup >		orderingGroups = new List< PWOrderingGroup >();
		
		[SerializeField]
		public HorizontalSplitView			h1;
		[SerializeField]
		public HorizontalSplitView			h2;
	
		[SerializeField]
		public Vector2						leftBarScrollPosition;
		[SerializeField]
		public Vector2						selectorScrollPosition;
	
		[SerializeField]
		public string						externalName;
		[SerializeField]
		public string						assetName;
		[SerializeField]
		public string						assetPath;
		[SerializeField]
		public string						saveName;
		[SerializeField]
		public Vector2						graphDecalPosition;
		[SerializeField]
		[HideInInspector]
		public int							localNodeIdCount;
		[SerializeField]
		[HideInInspector]
		public string						firstInitialization = null;
		[SerializeField]
		public bool							realMode;
		
		[SerializeField]
		[HideInInspector]
		public string						searchString = "";

		[SerializeField]
		public bool							presetChoosed;

		[SerializeField]
		public int							chunkSize;
		[SerializeField]
		public int							seed;
		[SerializeField]
		public float						step;
		[SerializeField]
		public float						maxStep;

		[SerializeField]
		public PWTerrainOutputMode					outputType;

		[SerializeField]
		public List< string >				subgraphReferences = new List< string >();
		[SerializeField]
		public string						parentReference;

		[SerializeField]
		public PWNode						inputNode;
		[SerializeField]
		public PWNode						outputNode;
		[SerializeField]
		public PWNode						externalGraphNode;

		//for GUI settings storage
		[SerializeField]
		public PWGUIManager					PWGUI;

		[System.NonSerializedAttribute]
		IOrderedEnumerable< PWNodeProcessInfo > computeOrderSortedNodes = null;

		[System.NonSerializedAttribute]
		public bool								unserializeInitialized = false;

		[System.NonSerializedAttribute]
		public Dictionary< string, PWNodeGraph >	graphInstancies = new Dictionary< string, PWNodeGraph >();
		[System.NonSerializedAttribute]
		public Dictionary< int, PWNode >			nodesDictionary = new Dictionary< int, PWNode >();

		[System.NonSerializedAttribute]
		Dictionary< string, Dictionary< string, FieldInfo > > bakedNodeFields = new Dictionary< string, Dictionary< string, FieldInfo > >();

		[System.NonSerializedAttribute]
		public bool			isVisibleInEditor = false;

		[System.NonSerializedAttribute]
		List< Type > allNodeTypeList = new List< Type > {
			typeof(PWNodeSlider), typeof(PWNodeTexture2D), typeof(PWNodeMaterial), typeof(PWNodeConstant), typeof(PWNodeMesh), typeof(PWNodeGameObject), typeof(PWNodeColor),
			typeof(PWNodeAdd),
			typeof(PWNodeDebugLog),
			typeof(PWNodeCircleNoiseMask),
			typeof(PWNodePerlinNoise2D),
			typeof(PWNodeSideView2DTerrain), typeof(PWNodeTopDown2DTerrain),
			typeof(PWNodeGraphInput), typeof(PWNodeGraphOutput), typeof(PWNodeGraphExternal),
			typeof(PWNodeBiomeData), typeof(PWNodeBiomeBinder), typeof(PWNodeWaterLevel),
			typeof(PWNodeBiomeBlender), typeof(PWNodeBiomeSwitch), typeof(PWNodeBiomeTemperature),
			typeof(PWNodeBiomeWetness),
		};
		
		private class PWNodeProcessInfo
		{
			public PWNode node;
			public string graphName;

			public PWNodeProcessInfo(PWNode n, string g) {
				node = n;
				graphName = g;
			}
		}

		void BakeNode(Type t)
		{
			var dico = new Dictionary< string, FieldInfo >();
			bakedNodeFields[t.AssemblyQualifiedName] = dico;
	
			foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				dico[field.Name] = field;
		}

		void InsertNodeIfNotExists(List< PWNodeProcessInfo > toComputeList, PWNode node)
		{
			if (toComputeList.Any(i => i.node.nodeId == node.nodeId))
				return ;
			
			toComputeList.Insert(0, new PWNodeProcessInfo(node, FindGraphNameFromExternalNode(node)));
		}

		void TryBuildGraphPart(PWNode leadNode, List< PWNodeProcessInfo > toComputeList, int depth = 0)
		{
			InsertNodeIfNotExists(toComputeList, leadNode);
			
			foreach (var dep in leadNode.GetDependencies())
			{
				var depNode = FindNodebyId(dep.nodeId);

				if (depNode.processMode == PWProcessMode.RequestForProcess)
				{
					InsertNodeIfNotExists(toComputeList, depNode);

					TryBuildGraphPart(depNode, toComputeList, depth++);
				}
			}
		}

		[System.NonSerializedAttribute]
		//store the list of nodes to becomputed per RequestForProcess nodes so if you ask to process
		// a node, it will just take the list from this dic. and compute them in the order of the list
		Dictionary< int, List< PWNodeProcessInfo > >	bakedGraphParts = new Dictionary< int, List< PWNodeProcessInfo > >();
		void BakeGraphParts(bool threaded = true)
		{
			//TODO: thread this, can be long

			ForeachAllNodes((n) => {
				if (!n)
					return ;
					
				var links = n.GetLinks();
				var deps = n.GetDependencies();

				if (links.Count > 0 && links.All(l => l.mode == PWProcessMode.RequestForProcess))
				{
					List< PWNodeProcessInfo > toComputeList;

					if (bakedGraphParts.ContainsKey(n.nodeId))
						toComputeList = bakedGraphParts[n.nodeId];
					else
						toComputeList = bakedGraphParts[n.nodeId] = new List< PWNodeProcessInfo >();
					
					//analyze of "extern groups" (group of node with only one RequestForProcess link at the end)
					if (deps.Any(d => FindNodebyId(d.nodeId).processMode == PWProcessMode.RequestForProcess))
					{
						TryBuildGraphPart(n, toComputeList);

						return ;
					}

					//go back to the farest node dependency with the RequestForProcess links:
					foreach (var depNodeId in n.GetDependencies().Select(d => d.nodeId).Distinct())
					{
						var node = FindNodebyId(depNodeId);

						if (node == null)
							continue ;
						
						if (node.GetLinks().All(dl => dl.mode == PWProcessMode.RequestForProcess))
							InsertNodeIfNotExists(toComputeList, node);
					}
					InsertNodeIfNotExists(toComputeList, n);

					foreach (var link in links.Where(l => l.mode == PWProcessMode.RequestForProcess).GroupBy(l => l.localNodeId).Select(g => g.First()))
					{
						PWNode node = FindNodebyId(link.distantNodeId);
					
						//if the node goes nowhere, add it
						if (node.GetLinks().Count == 0)
						{
							List< PWNodeProcessInfo > subToComputeList;
							if (bakedGraphParts.ContainsKey(node.nodeId))
								subToComputeList = bakedGraphParts[node.nodeId];
							else
								subToComputeList = bakedGraphParts[node.nodeId] = new List< PWNodeProcessInfo >();
							
							subToComputeList.RemoveAll(nodeInfo => toComputeList.Any(ni => ni.node.nodeId == nodeInfo.node.nodeId));

							subToComputeList.InsertRange(0, toComputeList);
							if (!subToComputeList.Any(ni => ni.node.nodeId == node.nodeId))
								subToComputeList.Insert(subToComputeList.Count, new PWNodeProcessInfo(node, FindGraphNameFromExternalNode(node)));
						}
					}
				}
			}, true, true);
			
			/*Debug.Log("created graph parts: " + bakedGraphParts.Count);
			foreach (var kp in bakedGraphParts)
			{
				Debug.Log("to compute list for node " + kp.Key);
				foreach (var ne in kp.Value)
					Debug.Log("\tne: " + ne.node.nodeId);
			}*/
		}

		public void RebakeGraphParts(bool graphRecursive = false)
		{
			foreach (var kp in bakedGraphParts)
				kp.Value.Clear();
			bakedGraphParts.Clear();
			BakeGraphParts(false);

			if (graphRecursive)
				foreach (var subgraphName in subgraphReferences)
				{
					var graph = FindGraphByName(subgraphName);

					graph.RebakeGraphParts(true);
				}
		}
	
		public void OnEnable()
		{
			//bake node fields to accelerate data transfer from node to node.
			bakedNodeFields.Clear();
			foreach (var nodeType in allNodeTypeList)
				BakeNode(nodeType);

			LoadGraphInstances();
			
			//add all existing nodes to the nodesDictionary
			for (int i = 0; i < nodes.Count; i++)
			{
				if (nodes[i] != null)
					nodesDictionary[nodes[i].nodeId] = nodes[i];
				else
				{
					nodes.RemoveAt(i);
					i--;
				}
			}
			foreach (var subgraphName in subgraphReferences)
			{
				var subgraph = FindGraphByName(subgraphName);

				if (subgraph != null && subgraph.externalGraphNode != null)
					nodesDictionary[subgraph.externalGraphNode.nodeId] = subgraph.externalGraphNode;
			}
			if (externalGraphNode != null)
				nodesDictionary[externalGraphNode.nodeId] = externalGraphNode;
			if (inputNode != null)
				nodesDictionary[inputNode.nodeId] = inputNode;
			if (outputNode != null)
				nodesDictionary[outputNode.nodeId] = outputNode;

			foreach (var node in nodes)
				node.UpdateCurrentGraph(this);
			if (inputNode != null)
				inputNode.UpdateCurrentGraph(this);
			if (outputNode != null)
				outputNode.UpdateCurrentGraph(this);
			
			//bake the graph parts (RequestForProcess links)
			BakeGraphParts();
		}

		public void	UpdateComputeOrder()
		{
			computeOrderSortedNodes = nodesDictionary
					//select all nodes building an object with node value and graph name (if needed)
					.Select(kp => new PWNodeProcessInfo(kp.Value, FindGraphNameFromExternalNode(kp.Value)))
					//sort the resulting list by computeOrder:
					.OrderBy(n => n.node.computeOrder);
		}

		void ProcessNodeLinks(PWNode node)
		{
			var links = node.GetLinks();

			foreach (var link in links)
			{
				if (!nodesDictionary.ContainsKey(link.distantNodeId))
					continue;
				
				if (link.mode == PWProcessMode.RequestForProcess)
					continue ;

				var target = nodesDictionary[link.distantNodeId];
	
				if (target == null)
					continue ;
	
				// Debug.Log("local: " + link.localClassAQName + " / " + node.GetType() + " / " + node.nodeId);
				// Debug.Log("distant: " + link.distantClassAQName + " / " + target.GetType() + " / " + target.nodeId);
				
				//ignore old links not removed cause of property removed in a script at compilation
				if (!realMode)
					if (!bakedNodeFields.ContainsKey(link.localClassAQName)
						|| !bakedNodeFields[link.localClassAQName].ContainsKey(link.localName)
						|| !bakedNodeFields[link.distantClassAQName].ContainsKey(link.distantName))
							continue ;

				var val = bakedNodeFields[link.localClassAQName][link.localName].GetValue(node);
				if (val == null)
					Debug.Log("null value of node: " + node.GetType() + " of field: " + link.localName);
				var prop = bakedNodeFields[link.distantClassAQName][link.distantName];

				//simple assignation, without multi-anchor
				if (link.distantIndex == -1 && link.localIndex == -1)
				{
					if (realMode)
						prop.SetValue(target, val);
					else
						try {
							prop.SetValue(target, val);
						} catch (Exception e) {
							Debug.LogError(e);
						}
				}
				else if (link.distantIndex != -1 && link.localIndex == -1) //distant link is a multi-anchor
				{
					PWValues values = (PWValues)prop.GetValue(target);
	
					if (values != null)
					{
						if (!values.AssignAt(link.distantIndex, val, link.localName))
							Debug.Log("failed to set distant indexed field value: " + link.distantName);
					}
				}
				else if (link.distantIndex == -1 && link.localIndex != -1 && val != null) //local link is a multi-anchor
				{
					object localVal = ((PWValues)val).At(link.localIndex);

					if (realMode)
						prop.SetValue(target, localVal);
					else
					{
						try {
							prop.SetValue(target, localVal);
						} catch {
							Debug.LogWarning("can't assign " + link.localName + " to " + link.distantName);
						}
					}
				}
				else if (val != null) // both are multi-anchors
				{
					PWValues values = (PWValues)prop.GetValue(target);
					object localVal = ((PWValues)val).At(link.localIndex);
	
					if (values != null)
					{
						// Debug.Log("assigned total multi");
						if (!values.AssignAt(link.distantIndex, localVal, link.localName))
							Debug.Log("failed to set distant indexed field value: " + link.distantName);
					}
				}
			}
		}

		float ProcessNode(PWNodeProcessInfo nodeInfo)
		{
			float	calculTime = 0;

			//if you are in editor mode, update the process time of the node
			if (!realMode)
			{
				Stopwatch	st = new Stopwatch();

				st.Start();
				nodeInfo.node.Process();
				st.Stop();

				nodeInfo.node.processTime = st.ElapsedMilliseconds;
				calculTime = nodeInfo.node.processTime;
			}
			else
				nodeInfo.node.Process();

			if (realMode || !isVisibleInEditor)
				nodeInfo.node.EndFrameUpdate();
			ProcessNodeLinks(nodeInfo.node);

			//if node was an external node, compute his subgraph
			if (nodeInfo.graphName != null)
			{
				PWNodeGraph g = FindGraphByName(nodeInfo.graphName);
				float time = g.ProcessGraph();
				if (!realMode)
					nodeInfo.node.processTime = time;
			}

			return calculTime;
		}

		public float ProcessGraph()
		{
			float		calculTime = 0f;

			if (computeOrderSortedNodes == null)
				UpdateComputeOrder();
			
			foreach (var nodeInfo in computeOrderSortedNodes)
			{
				//ignore unlink nodes
				if (nodeInfo.node.computeOrder < 0)
					continue ;
				
				if (realMode || !isVisibleInEditor)
					nodeInfo.node.BeginFrameUpdate();
				
				//if node outputs is only in RequestForProcess mode, avoid the computing
				var links = nodeInfo.node.GetLinks();
				if (links.Count > 0 && !links.Any(l => l.mode == PWProcessMode.AutoProcess))
					continue ;
				
				calculTime += ProcessNode(nodeInfo);
			}
			return calculTime;
		}

		public bool RequestProcessing(int nodeId)
		{
			if (bakedGraphParts.ContainsKey(nodeId))
			{
				var toComputeList = bakedGraphParts[nodeId];

				//compute the list
				foreach (var nodeInfo in toComputeList)
					ProcessNode(nodeInfo);

				return true;
			}
			return false;
		}

		public void	UpdateSeed(int seed)
		{
			this.seed = seed;
			ForeachAllNodes((n) => n.seed = seed, true, true);
		}

		public void UpdateChunkPosition(Vector3 chunkPos)
		{
			ForeachAllNodes((n) => n.chunkPosition = chunkPos, true, true);
		}

		public void UpdateChunkSize(int chunkSize)
		{
			this.chunkSize = chunkSize;
			ForeachAllNodes((n) => n.chunkSize = chunkSize, true, true);
		}

		public void UpdateStep(float step)
		{
			this.step = step;
			ForeachAllNodes((n) => n.step = step, true, true);
		}

		void LoadGraphInstances()
		{
			//load all available graph instancies in the AssetDatabase:
			if (!String.IsNullOrEmpty(assetPath))
			{
				int		resourceIndex = assetPath.IndexOf("Resources");
				if (resourceIndex != -1)
				{
					string resourcePath = Path.ChangeExtension(assetPath.Substring(resourceIndex + 10), null);
					var graphs = Resources.LoadAll(resourcePath, typeof(PWNodeGraph));
					foreach (var graph in graphs)
					{
						if (graphInstancies.ContainsKey(graph.name))
							continue ;
						graphInstancies.Add(graph.name, graph as PWNodeGraph);
					}
				}
			}
		}

		public PWNodeGraph FindGraphByName(string name)
		{
			PWNodeGraph		ret;
				
			if (name == null)
				return null;
			if (graphInstancies.TryGetValue(name, out ret))
				return ret;
			return null;
		}

		public string FindGraphNameFromExternalNode(PWNode node)
		{
			if (node.GetType() != typeof(PWNodeGraphExternal))
				return null;

            return subgraphReferences.FirstOrDefault(gName => {
                var g = FindGraphByName(gName);
                if (g.externalGraphNode.nodeId == node.nodeId)
                    return true;
                return false;
            });
		}

		public PWNode FindNodebyId(int nodeId)
		{
			if (nodesDictionary.ContainsKey(nodeId))
				return nodesDictionary[nodeId];
			return null;
		}

		public void ForeachAllNodes(System.Action< PWNode > callback, bool recursive = false, bool graphInputAndOutput = false, PWNodeGraph graph = null)
		{
			if (graph == null)
				graph = this;

			foreach (var node in graph.nodes)
				callback(node);

			foreach (var subgraphName in graph.subgraphReferences)
			{
				var g = FindGraphByName(subgraphName);

				if (g == null)
					continue ;
				
				callback(g.externalGraphNode);
				
				if (recursive)
					ForeachAllNodes(callback, recursive, graphInputAndOutput, g);
			}

			if (graphInputAndOutput)
			{
				callback(graph.inputNode);
				callback(graph.outputNode);
			}
		}
    }
}
