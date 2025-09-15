using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Experimental.Android.AppView;
using UnityEditor.Experimental.Android.AppView;
using Unity.Collections;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using UnityEditor.Build;
using Object = UnityEngine.Object;
using UnityEditor.AssetImporters;

namespace UnityEditor.UI
{
    [CustomEditor(typeof(AndroidAppView2D), true)]
    [CanEditMultipleObjects]
    public class AndroidAppView2DEditor : GraphicEditor
    {
        SerializedProperty m_AndroidAppViewSettings;

        protected override void OnEnable()
        {
            base.OnEnable();

            m_AndroidAppViewSettings = serializedObject.FindProperty("m_AndroidAppViewSettings");
        }

        public override void OnInspectorGUI()
        {
            AndroidAppViewEditorUtil.ExperimentalWarningGUI();

            serializedObject.Update();

            AndroidAppView2D androidAppView2D = target as AndroidAppView2D;
            AndroidAppViewSettings outSettings;
            var applyNewSettings = AndroidAppViewEditorUtil.AndroidAppViewSettingsGUI(m_AndroidAppViewSettings, out outSettings);
    
            AppearanceControlsGUI();
            RaycastControlsGUI();

            serializedObject.ApplyModifiedProperties();
            
            if (applyNewSettings)
            {
                if (outSettings != null)
                {
                    Material defaultMaterial = androidAppView2D.defaultMaterial;
                    Material view2dDefaultMaterial = outSettings.view2dDefaultMaterial;
                    if ((androidAppView2D.material == null || defaultMaterial == androidAppView2D.material) && view2dDefaultMaterial != null)
                    {
                        view2dDefaultMaterial.shader = defaultMaterial.shader;
                        androidAppView2D.material = view2dDefaultMaterial;
                    }
                }
                androidAppView2D.ApplyAndroidAppViewSettings();
            }
        }

        public override bool HasPreviewGUI()
        {
            AndroidAppView2D androidAppView2D = target as AndroidAppView2D;
            if (androidAppView2D == null)
                return false;

            var outer = new Rect(0f, 0f, 1f, 1f);
            return outer.width > 0 && outer.height > 0;
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            AndroidAppView2D androidAppView2D = target as AndroidAppView2D;
            Texture tex = androidAppView2D.mainTexture;

            if (tex == null)
                return;

            var outer = new Rect(0f, 0f, 1f, 1f);
            SpriteDrawUtility.DrawSprite(tex, rect, outer, outer, androidAppView2D.canvasRenderer.GetColor());
        }

        public override string GetInfoString()
        {
            AndroidAppView2D androidAppView2D = target as AndroidAppView2D;

            // Image size Text
            string text = string.Format("AndroidAppView2D Size: {0}x{1}",
                Mathf.RoundToInt(Mathf.Abs(androidAppView2D.rectTransform.rect.width)),
                Mathf.RoundToInt(Mathf.Abs(androidAppView2D.rectTransform.rect.height)));

            return text;
        }
    }
}
