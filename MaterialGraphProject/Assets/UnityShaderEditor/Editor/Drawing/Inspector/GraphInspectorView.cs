﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.UIElements;
using UnityEditor.Graphing.Util;
using UnityEngine;
using UnityEngine.Experimental.UIElements;
using UnityEditor.Graphing;

namespace UnityEditor.ShaderGraph.Drawing.Inspector
{
    public class GraphInspectorView : VisualElement, IDisposable
    {
        enum ResizeDirection
        {
            Any,
            Vertical,
            Horizontal
        }

        int m_SelectionHash;

        VisualElement m_PropertyItems;
        VisualElement m_LayerItems;
        VisualElement m_ContentContainer;
        ObjectField m_PreviewMeshPicker;
        AbstractNodeEditorView m_EditorView;

        TypeMapper m_TypeMapper;
        PreviewTextureView m_PreviewTextureView;

        AbstractMaterialGraph m_Graph;
        MasterNode m_MasterNode;
        PreviewRenderData m_PreviewRenderHandle;

        List<INode> m_SelectedNodes;

        Vector2 m_PreviewScrollPosition;

        public Action onUpdateAssetClick { get; set; }
        public Action onShowInProjectClick { get; set; }

        public GraphInspectorView(string assetName, PreviewManager previewManager, AbstractMaterialGraph graph)
        {
            m_Graph = graph;
            m_SelectedNodes = new List<INode>();

            AddStyleSheetPath("Styles/MaterialGraph");

            var topContainer = new VisualElement {name = "top"};
            {
                var headerContainer = new VisualElement {name = "header"};
                {
                    var title = new Label(assetName) {name = "title"};
                    title.AddManipulator(new Clickable(() =>
                    {
                        if (onShowInProjectClick != null)
                            onShowInProjectClick();
                    }));
                    headerContainer.Add(title);
                    headerContainer.Add(new Button(() =>
                    {
                        if (onUpdateAssetClick != null)
                            onUpdateAssetClick();
                    }) { name = "save", text = "Save" });
                }
                topContainer.Add(headerContainer);

                m_ContentContainer = new VisualElement {name = "content"};
                topContainer.Add(m_ContentContainer);
            }
            Add(topContainer);

            var bottomContainer = new VisualElement {name = "bottom"};
            {
                var propertiesContainer = new VisualElement {name = "properties"};
                {
                    var header = new VisualElement {name = "header"};
                    {
                        var title = new Label("Properties") {name = "title"};
                        header.Add(title);

                        var addPropertyButton = new Button(OnAddProperty) {text = "Add", name = "addButton"};
                        header.Add(addPropertyButton);
                    }
                    propertiesContainer.Add(header);

                    m_PropertyItems = new VisualContainer {name = "items"};
                    propertiesContainer.Add(m_PropertyItems);
                }
                bottomContainer.Add(propertiesContainer);

                m_PreviewTextureView = new PreviewTextureView { name = "preview", image = Texture2D.blackTexture };
                m_PreviewTextureView.AddManipulator(new Draggable(OnMouseDrag, true));
                bottomContainer.Add(m_PreviewTextureView);

                m_PreviewScrollPosition = new Vector2(0f, 0f);

                m_PreviewMeshPicker = new ObjectField() { objectType = typeof(Mesh) };
                m_PreviewMeshPicker.OnValueChanged(OnPreviewMeshChanged);

                bottomContainer.Add(m_PreviewMeshPicker);
            }
            Add(bottomContainer);

            m_PreviewRenderHandle = previewManager.masterRenderData;
            m_PreviewRenderHandle.onPreviewChanged += OnPreviewChanged;

            m_PreviewMeshPicker.SetValueAndNotify(m_Graph.previewData.serializedMesh.mesh);

            foreach (var property in m_Graph.properties)
                m_PropertyItems.Add(new ShaderPropertyView(m_Graph, property));

            var resizeHandleTop = new Label { name = "resize-top", text = "" };
            resizeHandleTop.AddManipulator(new Draggable(mouseDelta => OnResize(mouseDelta, ResizeDirection.Vertical, true)));
            Add(resizeHandleTop);

            var resizeHandleRight = new Label { name = "resize-right", text = "" };
            resizeHandleRight.AddManipulator(new Draggable(mouseDelta => OnResize(mouseDelta, ResizeDirection.Horizontal, false)));
            Add(resizeHandleRight);

            var resizeHandleLeft = new Label { name = "resize-left", text = "" };
            resizeHandleLeft.AddManipulator(new Draggable(mouseDelta => OnResize(mouseDelta, ResizeDirection.Horizontal, true)));
            Add(resizeHandleLeft);

            var resizeHandleBottom = new Label { name = "resize-bottom", text = "" };
            resizeHandleBottom.AddManipulator(new Draggable(mouseDelta => OnResize(mouseDelta, ResizeDirection.Vertical, false)));
            Add(resizeHandleBottom);

            // Nodes missing custom editors:
            // - PropertyNode
            // - SubGraphInputNode
            // - SubGraphOutputNode
            m_TypeMapper = new TypeMapper(typeof(INode), typeof(AbstractNodeEditorView), typeof(StandardNodeEditorView))
            {
                // { typeof(AbstractSurfaceMasterNode), typeof(SurfaceMasterNodeEditorView) }
            };
        }

        void OnResize(Vector2 resizeDelta, ResizeDirection direction, bool moveWhileResize)
        {
            Vector2 normalizedResizeDelta = resizeDelta / 2f;

            if (direction == ResizeDirection.Vertical)
            {
                normalizedResizeDelta.x = 0f;
            }
            else if (direction == ResizeDirection.Horizontal)
            {
                normalizedResizeDelta.y = 0f;
            }

            Vector2 newSize = new Vector2();
            Vector2 newPosition = new Vector2(style.positionLeft, style.positionTop);

            if (moveWhileResize)
            {
                newPosition.x = style.positionLeft + normalizedResizeDelta.x;
                newPosition.y = style.positionTop + normalizedResizeDelta.y;

                normalizedResizeDelta *= -1f;
            }

            newSize.x = Mathf.Max(style.width + normalizedResizeDelta.x, 60f);
            newSize.y = Mathf.Max(style.height + normalizedResizeDelta.y, 60f);

            if (newSize.x > 60f)
            {
                style.positionLeft = newPosition.x;
                style.width = Mathf.Max(style.width + normalizedResizeDelta.x, 60f);
            }

            if (newSize.y > 60f)
            {
                style.positionTop = newPosition.y;
                style.height = Mathf.Max(style.height + normalizedResizeDelta.y, 60f);
            }
        }

        MasterNode masterNode
        {
            get { return m_PreviewRenderHandle.shaderData.node as MasterNode; }
        }

        void OnMouseDrag(Vector2 deltaMouse)
        {
            Vector2 previewSize = m_PreviewTextureView.contentRect.size;

            m_PreviewScrollPosition -= deltaMouse * (Event.current.shift ? 3f : 1f) / Mathf.Min(previewSize.x, previewSize.y) * 140f;
            m_PreviewScrollPosition.y = Mathf.Clamp(m_PreviewScrollPosition.y, -90f, 90f);
            Quaternion previewRotation = Quaternion.Euler(m_PreviewScrollPosition.y, 0, 0) * Quaternion.Euler(0, m_PreviewScrollPosition.x, 0);
            m_Graph.previewData.rotation = previewRotation;

            masterNode.onModified(masterNode, ModificationScope.Node);
        }

        void OnAddProperty()
        {
            var gm = new GenericMenu();
            gm.AddItem(new GUIContent("Float"), false, () => AddProperty(new FloatShaderProperty()));
            gm.AddItem(new GUIContent("Vector2"), false, () => AddProperty(new Vector2ShaderProperty()));
            gm.AddItem(new GUIContent("Vector3"), false, () => AddProperty(new Vector3ShaderProperty()));
            gm.AddItem(new GUIContent("Vector4"), false, () => AddProperty(new Vector4ShaderProperty()));
            gm.AddItem(new GUIContent("Color"), false, () => AddProperty(new ColorShaderProperty()));
            gm.AddItem(new GUIContent("Texture"), false, () => AddProperty(new TextureShaderProperty()));
            gm.AddItem(new GUIContent("Cubemap"), false, () => AddProperty(new CubemapShaderProperty()));
            gm.ShowAsContext();
        }

        void AddProperty(IShaderProperty property)
        {
            m_Graph.owner.RegisterCompleteObjectUndo("Add Property");
            m_Graph.AddShaderProperty(property);
        }

        void OnPreviewChanged()
        {
            m_PreviewTextureView.image = m_PreviewRenderHandle.texture ?? Texture2D.blackTexture;
            m_PreviewTextureView.Dirty(ChangeType.Repaint);
        }

        void OnPreviewMeshChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            Mesh changedMesh = changeEvent.newValue as Mesh;

            masterNode.onModified(masterNode, ModificationScope.Node);

            if (m_Graph.previewData.serializedMesh.mesh != changedMesh)
            {
                m_Graph.previewData.rotation = Quaternion.identity;
            }

            m_Graph.previewData.serializedMesh.mesh = changedMesh;
        }

        public void UpdateSelection(IEnumerable<INode> nodes)
        {
            m_SelectedNodes.Clear();
            m_SelectedNodes.AddRange(nodes);

            var selectionHash = UIUtilities.GetHashCode(m_SelectedNodes.Count,
                    m_SelectedNodes != null ? m_SelectedNodes.FirstOrDefault() : null);
            if (selectionHash != m_SelectionHash)
            {
                m_SelectionHash = selectionHash;
                m_ContentContainer.Clear();
                if (m_SelectedNodes.Count > 1)
                {
                    var element = new Label(string.Format("{0} nodes selected.", m_SelectedNodes.Count))
                    {
                        name = "selectionCount"
                    };
                    m_ContentContainer.Add(element);
                }
                else if (m_SelectedNodes.Count == 1)
                {
                    var node = m_SelectedNodes.First();
                    var view = (AbstractNodeEditorView)Activator.CreateInstance(m_TypeMapper.MapType(node.GetType()));
                    view.node = node;
                    m_ContentContainer.Add(view);
                }
            }
        }

        public void HandleGraphChanges()
        {
            foreach (var propertyGuid in m_Graph.removedProperties)
            {
                var propertyView = m_PropertyItems.OfType<ShaderPropertyView>().FirstOrDefault(v => v.property.guid == propertyGuid); if (propertyView != null)
                    m_PropertyItems.Remove(propertyView);
            }

            foreach (var property in m_Graph.addedProperties)
                m_PropertyItems.Add(new ShaderPropertyView(m_Graph, property));
        }

        public void Dispose()
        {
            if (m_PreviewRenderHandle != null)
            {
                m_PreviewRenderHandle.onPreviewChanged -= OnPreviewChanged;
                m_PreviewRenderHandle = null;
            }
        }
    }
}
