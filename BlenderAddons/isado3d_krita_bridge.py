bl_info = {
    "name": "Isa do 3D - Krita Bridge",
    "author": "Isa",
    "version": (1, 0, 0),
    "blender": (3, 4, 0),
    "location": "Image Editor > Sidebar > Isa do 3D, Image > Edit Texture in Krita",
    "description": "Opens the active texture in Krita and auto-reloads it in Blender when saved.",
    "category": "Paint",
}

import bpy
import os
import subprocess
from bpy.types import Operator, Panel, AddonPreferences
from bpy.props import StringProperty, FloatProperty

ADDON_ID = __name__

_watched_images = {}
_pending_mtimes = {}
_timer_running = False


def find_krita_default():
    candidates = [
        r"C:\Program Files\Krita (x64)\bin\krita.exe",
        r"C:\Program Files\Krita\bin\krita.exe",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    return ""


class IsaDo3DKritaPreferences(AddonPreferences):
    bl_idname = ADDON_ID

    krita_path: StringProperty(
        name="Krita path",
        subtype='FILE_PATH',
        default=find_krita_default(),
    )

    check_interval: FloatProperty(
        name="Check interval (seconds)",
        default=1.0,
        min=0.2,
        max=10.0,
    )

    def draw(self, context):
        layout = self.layout
        layout.prop(self, "krita_path")
        layout.prop(self, "check_interval")


def get_active_image(context):
    if context.area and context.area.type == 'IMAGE_EDITOR' and context.space_data.image:
        return context.space_data.image

    # Fallback so it also works from the viewport
    obj = context.active_object
    if obj and obj.active_material and obj.active_material.use_nodes:
        for node in obj.active_material.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                return node.image

    return None


class ISADO3D_OT_edit_in_krita(Operator):
    bl_idname = "isado3d.edit_in_krita"
    bl_label = "Edit Texture in Krita"
    bl_description = "Opens the active texture in Krita and auto-reloads it in Blender when saved"

    def execute(self, context):
        image = get_active_image(context)
        if image is None:
            self.report({'ERROR'}, "No active texture found (open one in the Image Editor or select an object with an Image Texture in its material).")
            return {'CANCELLED'}

        if not image.filepath:
            self.report({'ERROR'}, "This texture has no file saved on disk (it's generated or only packed).")
            return {'CANCELLED'}

        filepath = bpy.path.abspath(image.filepath)
        if not os.path.exists(filepath):
            self.report({'ERROR'}, f"File not found: {filepath}")
            return {'CANCELLED'}

        krita_path = context.preferences.addons[ADDON_ID].preferences.krita_path
        if not krita_path or not os.path.exists(krita_path):
            self.report({'ERROR'}, "Krita path not configured. Set it in Edit > Preferences > Add-ons > Isa do 3D - Krita Bridge.")
            return {'CANCELLED'}

        try:
            subprocess.Popen([krita_path, filepath])
        except Exception as e:
            self.report({'ERROR'}, f"Failed to open Krita: {e}")
            return {'CANCELLED'}

        _watched_images[image.name] = os.path.getmtime(filepath)
        _pending_mtimes.pop(image.name, None)
        _ensure_watcher_running()

        self.report({'INFO'}, f"Opening '{os.path.basename(filepath)}' in Krita. It will reload here automatically when you save.")
        return {'FINISHED'}


def _check_watched_images():
    interval = bpy.context.preferences.addons[ADDON_ID].preferences.check_interval

    for image in list(bpy.data.images):
        if image.name not in _watched_images or not image.filepath:
            continue

        filepath = bpy.path.abspath(image.filepath)
        if not os.path.exists(filepath):
            continue

        mtime = os.path.getmtime(filepath)
        if mtime == _watched_images[image.name]:
            _pending_mtimes.pop(image.name, None)
            continue

        # Krita changes the mtime as soon as it starts saving, so wait for it to settle
        if _pending_mtimes.get(image.name) != mtime:
            _pending_mtimes[image.name] = mtime
            continue

        _watched_images[image.name] = mtime
        _pending_mtimes.pop(image.name, None)
        image.reload()
        # bpy.context.screen can be None inside a timer
        for window in bpy.context.window_manager.windows:
            for area in window.screen.areas:
                if area.type in {'IMAGE_EDITOR', 'VIEW_3D', 'NODE_EDITOR'}:
                    area.tag_redraw()
        print(f"[IsaDo3D Krita Bridge] Reloaded: {image.name}")

    return interval


def _ensure_watcher_running():
    global _timer_running
    if not _timer_running:
        bpy.app.timers.register(_check_watched_images, persistent=True)
        _timer_running = True


class ISADO3D_PT_krita_bridge(Panel):
    bl_label = "Isa do 3D"
    bl_space_type = 'IMAGE_EDITOR'
    bl_region_type = 'UI'
    bl_category = "Isa do 3D"

    def draw(self, context):
        layout = self.layout
        image = get_active_image(context)
        if image:
            layout.label(text=image.name, icon='IMAGE_DATA')
        layout.operator(ISADO3D_OT_edit_in_krita.bl_idname, icon='TOOL_SETTINGS')
        if image and image.name in _watched_images:
            layout.label(text="Watching for changes...", icon='FILE_REFRESH')


def _menu_func(self, context):
    self.layout.operator(ISADO3D_OT_edit_in_krita.bl_idname, icon='TOOL_SETTINGS')


classes = (
    IsaDo3DKritaPreferences,
    ISADO3D_OT_edit_in_krita,
    ISADO3D_PT_krita_bridge,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.IMAGE_MT_image.append(_menu_func)


def unregister():
    global _timer_running
    bpy.types.IMAGE_MT_image.remove(_menu_func)
    if _timer_running and bpy.app.timers.is_registered(_check_watched_images):
        bpy.app.timers.unregister(_check_watched_images)
    _timer_running = False
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
