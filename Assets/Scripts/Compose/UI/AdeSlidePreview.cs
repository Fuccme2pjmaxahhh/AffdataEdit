using System;
using System.Collections.Generic;
using Arcade.Gameplay.Chart;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Arcade.Compose.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AdeSlidePreview : Graphic, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [System.NonSerialized]
        public ArcSlide Note;
        public Action<ArcSlide> OnEdited;
        private ArcSlide edit;
        private int corner;
        private IReadOnlyList<float> gridPositions;
        private float gridScaleFactor = 1f;

        private void Update()
        {
            var positions = AdeGridManager.Instance ? AdeGridManager.Instance.VerticalXPositions : null;
            float scaleFactor = canvas ? canvas.scaleFactor : 1f;
            if (ReferenceEquals(gridPositions, positions) && gridScaleFactor == scaleFactor) return;
            gridPositions = positions;
            gridScaleFactor = scaleFactor;
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (Note == null) return;
            var r = rectTransform.rect;
            Quad(vh, new Vector2(r.xMin,r.yMin), new Vector2(r.xMax,r.yMin), new Vector2(r.xMin,r.yMax), new Vector2(r.xMax,r.yMax), new Color(.1f,.09f,.15f));
            for(int i=0;i<32;i++)
            {
                float a=i/32f,b=(i+1)/32f; var ra=Note.Range(a);var rb=Note.Range(b);
                Quad(vh, Point(ra.x,a),Point(ra.y,a),Point(rb.x,b),Point(rb.y,b),new Color(.65f,.43f,.95f,.8f));
            }
            // Use the same parsed X positions as the editor grid, including custom lines.
            if (gridPositions != null) foreach (float x in gridPositions)
            {
                if (x < ArcSlide.MinX || x > ArcSlide.MaxX) continue;
                var bottom = Point(x, 0);
                var top = Point(x, 1);
                // A UI unit can be less than a screen pixel when the window is scaled down.
                var width = new Vector2(1f / gridScaleFactor, 0);
                bottom -= width / 2;
                top -= width / 2;
                if (canvas)
                {
                    bottom = RectTransformUtility.PixelAdjustPoint(bottom, rectTransform, canvas);
                    top = RectTransformUtility.PixelAdjustPoint(top, rectTransform, canvas);
                }
                Quad(vh, bottom, bottom + width, top, top + width,
                    new Color(.85f, .85f, .95f, .75f));
            }
            foreach(float t in new[]{0f,1f})
            {
                var range=Note.Range(t);
                foreach(float x in new[]{range.x,range.y})
                {
                    var p=Point(x,t);Quad(vh,p-new Vector2(4,4),p+new Vector2(4,-4),p+new Vector2(-4,4),p+new Vector2(4,4),Color.white);
                }
            }
        }
        private Vector2 Point(float x,float t) => new Vector2(rectTransform.rect.xMin+8+(x-ArcSlide.MinX)/(ArcSlide.MaxX-ArcSlide.MinX)*(rectTransform.rect.width-16),rectTransform.rect.yMin+8+t*(rectTransform.rect.height-16));
        private static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {
            int n=vh.currentVertCount; vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddVert(d,color,Vector2.zero);
            vh.AddTriangle(n,n+2,n+1);vh.AddTriangle(n+1,n+2,n+3);
        }
        public void OnBeginDrag(PointerEventData e)
        {
            if(Note==null)return; edit=(ArcSlide)Note.Clone();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var p);
            float best=float.MaxValue;
            for(int i=0;i<4;i++){var range=Note.Range(i/2);float x=i%2==0?range.x:range.y;float d=(Point(x,i/2)-p).sqrMagnitude;if(d<best){best=d;corner=i;}}
        }
        public void OnDrag(PointerEventData e)
        {
            if(edit==null)return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera,out var p);
            float x=Mathf.LerpUnclamped(ArcSlide.MinX, ArcSlide.MaxX, (p.x-rectTransform.rect.xMin-8)/(rectTransform.rect.width-16));
            float t=corner/2;var range=edit.Range(t);
            if(corner%2==0)range.x=SnapX(x, float.NegativeInfinity, range.y-1f/48);
            else range.y=SnapX(x, range.x+1f/48, float.PositiveInfinity);
            if(corner<2){edit.StartCenter=(range.x+range.y)/2;edit.StartWidth=range.y-range.x;}else{edit.EndCenter=(range.x+range.y)/2;edit.EndWidth=range.y-range.x;}
            Note=edit;SetVerticesDirty();
        }
        private float SnapX(float x, float min, float max)
        {
            x = Mathf.Clamp(x, min, max);
            float nearest = x, distance = .3f; // Same capture distance as the editor's X grid.
            var positions = AdeGridManager.Instance ? AdeGridManager.Instance.VerticalXPositions : null;
            if (positions == null) return x;
            foreach (float line in positions)
            {
                if (line < min || line > max) continue;
                float delta = Mathf.Abs(line - x);
                if (delta < distance) { distance = delta; nearest = line; }
            }
            return nearest;
        }

        public void OnEndDrag(PointerEventData e){if(edit!=null){OnEdited?.Invoke(edit);edit=null;}}
    }
}
