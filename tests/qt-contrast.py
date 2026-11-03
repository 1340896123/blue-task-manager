"""Measure the actual native theme tokens with the WCAG relative luminance formula."""
import json
from pathlib import Path
import re
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--output')
args = parser.parse_args()

root = Path(__file__).resolve().parents[1]
source = (root / 'src/Takao.Qt/Theme.h').read_text(encoding='utf-8')
colors = dict(re.findall(r'(\w+)\((?:QStringLiteral\()?"(#[0-9A-Fa-f]{6})"\)(?:\))?', source))


def luminance(color):
    rgb = [int(color[index:index+2], 16)/255 for index in (1, 3, 5)]
    linear = [v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4 for v in rgb]
    return sum(value*weight for value, weight in zip(linear, (.2126, .7152, .0722)))


checks = []
pairs = [
    ('text', 'surface', 4.5), ('secondary', 'surface', 4.5),
    ('secondary', 'app', 4.5), ('text', 'selection', 4.5),
    ('accent', 'selection', 4.5), ('surface', 'accent', 4.5),
    ('danger', 'surface', 4.5), ('warning', 'surface', 4.5),
    ('danger', 'dangerSurface', 4.5),
    ('accent', 'surface', 3), ('secondLine', 'surface', 3),
    ('focus', 'surface', 3), ('focus', 'selection', 3), ('border', 'surface', 3),
]
# Additional Fluent surfaces are checked when defined by the current theme.
for front, back, threshold in [
    ('text', 'content', 4.5), ('secondary', 'content', 4.5),
    ('text', 'hover', 4.5), ('focus', 'hover', 3),
    ('success', 'successSurface', 4.5), ('warning', 'warningSurface', 4.5),
    ('accent', 'infoSurface', 4.5), ('secondary', 'surfaceSubtle', 4.5),
    ('surface', 'accentHover', 4.5), ('surface', 'accentPressed', 4.5),
    ('surface', 'dangerHover', 4.5), ('surface', 'dangerPressed', 4.5),
    ('text', 'accent', 3),
]:
    if front in colors and back in colors:
        pairs.append((front, back, threshold))
heat = re.search(r'maximumHeat\s*=\s*([.\d]+)', source)
if heat:
    alpha = float(heat.group(1))
    rgb = lambda color: [int(color[index:index+2], 16) for index in (1, 3, 5)]
    colors['heatMaximum'] = '#' + ''.join(f'{round(f*alpha+b*(1-alpha)):02X}' for f,b in zip(rgb(colors['accent']),rgb(colors['surface'])))
    pairs.extend([('text','heatMaximum',4.5),('secondary','heatMaximum',4.5),('focus','heatMaximum',3)])
for front, back, threshold in pairs:
    lighter, darker = sorted((luminance(colors[front]), luminance(colors[back])), reverse=True)
    ratio = (lighter+.05)/(darker+.05)
    checks.append({'foreground': front, 'background': back, 'ratio': round(ratio, 3),
                   'minimum': threshold, 'pass': ratio >= threshold})
report = {'result': 'PASS' if all(item['pass'] for item in checks) else 'FAIL', 'checks': checks}
output = Path(args.output).resolve() if args.output else root / 'output/qt-acceptance/contrast.json'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
if report['result'] != 'PASS':
    raise SystemExit(1)
