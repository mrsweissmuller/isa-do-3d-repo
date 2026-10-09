bl_info = {
    "name": "Isa do 3D - Auto LOD",
    "author": "Isa",
    "version": (1, 0, 0),
    "blender": (3, 4, 0),
    "location": "View3D > Sidebar > Isa do 3D > Auto LOD",
    "description": "Generates an LOD chain (decimate) for the selected meshes, with _LOD0/1/2 naming the engine recognizes. Preserves the UV maps and organizes the hierarchy export-ready.",
    "category": "Object",
}

import bpy
import re
from bpy.types import Operator, Panel, AddonPreferences
from bpy.props import BoolProperty, FloatProperty, IntProperty

ADDON_ID = __name__

_DECIMATE_MOD = "IsaLOD_Decimate"
_LOD_SUFFIX_RE = re.compile(r'_LOD\d+$')

_last_report = []


class IsaDo3DAutoLODPreferences(AddonPreferences):
    bl_idname = ADDON_ID

    levels: IntProperty(
        name="Reduced LODs",
        description="How many reduced levels to generate beyond LOD0 (the original). 2 => LOD1 and LOD2",
        default=2,
        min=1,
        max=5,
    )

    ratio_step: FloatProperty(
        name="Factor per level",
        description="Fraction of faces kept at each level relative to the previous one. 0.5 => LOD1=50%, LOD2=25%...",
        default=0.5,
        min=0.05,
        max=0.95,
    )

    apply_modifier: BoolProperty(
        name="Apply the decimate",
        description="Applies each LOD's modifier (final mesh). Off leaves the Decimate live for manual tweaking.",
        default=True,
    )

    rename_source_lod0: BoolProperty(
        name="Rename original to _LOD0",
        description="Renames the original object to {base}_LOD0, keeping the chain consistent for the engine",
        default=True,
    )

    group_under_empty: BoolProperty(
        name="Group under an Empty",
        description="Creates an Empty '{base}' as the parent of all LODs - hierarchy ready for the LODGroup in Unity",
        default=True,
    )

    triangulate: BoolProperty(
        name="Triangulate result",
        description="Adds a Triangulate after the Decimate so the tri count matches the engine",
        default=False,
    )

    def draw(self, context):
        layout = self.layout
        col = layout.column(align=True)
        col.prop(self, "levels")
        col.prop(self, "ratio_step")
        layout.separator()
        col = layout.column(align=True)
        col.prop(self, "apply_modifier")
        col.prop(self, "rename_source_lod0")
        col.prop(self, "group_under_empty")
        col.prop(self, "triangulate")


def _get_prefs(context):
    return context.preferences.addons[ADDON_ID].preferences


def _base_name(name):
    return _LOD_SUFFIX_RE.sub('', name)


def _tri_count(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def _duplicate_mesh_object(src, new_name, context):
    dup = src.copy()
    dup.data = src.data.copy()
    dup.name = new_name
    dup.data.name = new_name
    colls = src.users_collection or (context.scene.collection,)
    for coll in colls:
        coll.objects.link(dup)
    return dup


def _apply_modifiers(obj, mod_names, context):
    with context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        for name in mod_names:
            if name in obj.modifiers:
                bpy.ops.object.modifier_apply(modifier=name)


def _build_lod(src, level, ratio, prefs, context):
    name = f"{_base_name(src.name)}_LOD{level}"
    dup = _duplicate_mesh_object(src, name, context)

    dec = dup.modifiers.new(_DECIMATE_MOD, 'DECIMATE')
    dec.decimate_type = 'COLLAPSE'
    dec.ratio = ratio
    dec.use_collapse_triangulate = False  # Keeps quads and UVs

    mod_order = [_DECIMATE_MOD]
    if prefs.triangulate:
        tri = dup.modifiers.new("IsaLOD_Triangulate", 'TRIANGULATE')
        mod_order.append(tri.name)

    if prefs.apply_modifier:
        _apply_modifiers(dup, mod_order, context)
        tris = _tri_count(dup.data)
    else:
        # Not applied, dup.data still has the full mesh
        evaluated = dup.evaluated_get(context.evaluated_depsgraph_get())
        tris = _tri_count(evaluated.to_mesh())
        evaluated.to_mesh_clear()
    return dup, tris


def _parent_keep_transform(child, parent):
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def _source_objects(context):
    return [o for o in context.selected_objects if o.type == 'MESH']


class ISADO3D_OT_generate_lods(Operator):
    bl_idname = "isado3d.generate_lods"
    bl_label = "Generate LODs"
    bl_description = ("Creates the LOD chain (decimate) for the selected meshes, "
                      "with _LOD0/1/2 naming and an export-ready hierarchy.")
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return (context.mode == 'OBJECT'
                and any(o.type == 'MESH' for o in context.selected_objects))

    def execute(self, context):
        global _last_report
        prefs = _get_prefs(context)

        sources = _source_objects(context)
        if not sources:
            self.report({'WARNING'}, "Select at least one mesh.")
            return {'CANCELLED'}

        _last_report = []
        created = 0

        for src in sources:
            base = _base_name(src.name)

            # Decimating an already reduced LOD degrades it further
            m = _LOD_SUFFIX_RE.search(src.name)
            if m and not src.name.endswith("_LOD0"):
                _last_report.append({"base": src.name,
                                     "skipped": "already a reduced LOD - select the LOD0/original"})
                continue

            existing = f"{base}_LOD1"
            if existing in bpy.data.objects:
                _last_report.append({"base": base,
                                     "skipped": f"'{existing}' already exists - delete the old chain first"})
                continue

            levels_info = []

            lod0 = src
            if prefs.rename_source_lod0 and not src.name.endswith("_LOD0"):
                lod0.name = f"{base}_LOD0"
                if lod0.data.users == 1:
                    lod0.data.name = lod0.name
            levels_info.append((lod0.name, _tri_count(lod0.data)))

            lod_objects = [lod0]

            ratio = 1.0
            for level in range(1, prefs.levels + 1):
                ratio *= prefs.ratio_step
                lod_obj, tris = _build_lod(src, level, ratio, prefs, context)
                lod_objects.append(lod_obj)
                levels_info.append((lod_obj.name, tris))
                created += 1

            # Unity builds the LODGroup from this Empty
            if prefs.group_under_empty:
                empty = bpy.data.objects.new(base, None)
                empty.matrix_world = lod0.matrix_world.copy()
                colls = lod0.users_collection or (context.scene.collection,)
                for coll in colls:
                    coll.objects.link(empty)
                for obj in lod_objects:
                    _parent_keep_transform(obj, empty)

            _last_report.append({"base": base, "levels": levels_info})

        if created == 0:
            self.report({'WARNING'}, "Nothing generated (see the panel for the reasons).")
            return {'CANCELLED'}

        self.report({'INFO'}, f"{created} LOD(s) generated for {len(sources)} mesh(es).")
        return {'FINISHED'}


class ISADO3D_OT_clear_lods(Operator):
    bl_idname = "isado3d.clear_lods"
    bl_label = "Delete reduced LODs"
    bl_description = ("Deletes the _LOD1+ objects of the selected bases (keeps the _LOD0/original). "
                      "Useful to regenerate from scratch.")
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and bool(context.selected_objects)

    def execute(self, context):
        bases = {_base_name(o.name) for o in context.selected_objects}
        to_remove = []
        for obj in bpy.data.objects:
            m = _LOD_SUFFIX_RE.search(obj.name)
            if not m:
                continue
            if obj.name.endswith("_LOD0"):
                continue
            if _base_name(obj.name) in bases:
                to_remove.append(obj)

        for obj in to_remove:
            bpy.data.objects.remove(obj, do_unlink=True)

        self.report({'INFO'}, f"{len(to_remove)} reduced LOD(s) deleted.")
        return {'FINISHED'}


class ISADO3D_PT_auto_lod(Panel):
    bl_label = "Auto LOD"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Isa do 3D"

    def draw(self, context):
        layout = self.layout
        prefs = _get_prefs(context)

        n_sel = len([o for o in context.selected_objects if o.type == 'MESH'])
        layout.label(text=f"Source: {n_sel} mesh(es) selected", icon='MOD_DECIM')

        row = layout.row(align=True)
        row.prop(prefs, "levels", text="Levels")
        row.prop(prefs, "ratio_step", text="Factor")

        layout.operator(ISADO3D_OT_generate_lods.bl_idname, icon='MOD_DECIM')
        layout.operator(ISADO3D_OT_clear_lods.bl_idname, icon='TRASH')

        if _last_report:
            layout.separator()
            box = layout.box()
            for entry in _last_report:
                col = box.column(align=True)
                if "skipped" in entry:
                    col.label(text=f"{entry['base']}: skipped", icon='X')
                    col.label(text=entry["skipped"], icon='BLANK1')
                    continue
                col.label(text=entry["base"], icon='OUTLINER_OB_EMPTY')
                for name, tris in entry["levels"]:
                    col.label(text=f"{name} - {tris} tris", icon='DOT')


classes = (
    IsaDo3DAutoLODPreferences,
    ISADO3D_OT_generate_lods,
    ISADO3D_OT_clear_lods,
    ISADO3D_PT_auto_lod,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)


def unregister():
    global _last_report
    _last_report = []
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
