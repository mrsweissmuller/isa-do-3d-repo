using UnityEditor;
using UnityEngine;

namespace IsaDo3D.EnvironmentArt
{
    internal static class IsaDo3DStyles
    {
        private static GUIStyle _title;

        // Criado no primeiro uso, EditorStyles só existe depois que a GUI carrega
        public static GUIStyle Title => _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
    }
}
