# La finestra del .dmg di DuckNote, per dmgbuild (build-app.sh la usa sul Mac):
#   dmgbuild -s dmg_settings.py -D app=<DuckNote.app> -D background=<png> -D icon=<icns> <volume> <dmg>
# Lo sfondo e' disegnato da make_dmg_background.py: 660x400, con la versione @2x accanto
# (dmgbuild le unisce da se'); le posizioni delle icone sono le stesse del disegno.
# "defines" lo mette dmgbuild fra i globali di questo file.

application = defines["app"]  # noqa: F821

format = "UDZO"
filesystem = "HFS+"
files = [application]
symlinks = {"Applicazioni": "/Applications"}
hide_extensions = ["DuckNote.app"]
icon = defines.get("icon")  # noqa: F821  - l'icona del volume, quella dell'app
background = defines["background"]  # noqa: F821

# 400 punti di disegno piu' i 28 della barra del titolo, che la finestra comprende
window_rect = ((200, 120), (660, 428))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
show_icon_preview = False
include_icon_view_settings = True
include_list_view_settings = False

icon_size = 112
text_size = 13
icon_locations = {
    "DuckNote.app": (165, 200),
    "Applicazioni": (495, 200),
}
