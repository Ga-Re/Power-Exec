"""Erzeugt src/PowerExec/Assets/icons.json aus Heroicons (24/outline) und Fluent UI System Icons (24 regular).

Aufruf: python3 -I tools/build_icons.py <heroicons-package-dir> <fluent-svg-icons-package-dir>
Beide Pakete stehen unter der MIT-Lizenz (siehe THIRD-PARTY-NOTICES.md).
Die Pfaddaten werden für den WPF-Parser normalisiert (Leerzeichen zwischen allen Zahlen, Bogen-Flags einzeln).
"""
import json, os, re, sys

CMD = set('MmLlHhVvCcSsQqTtAaZz')
NUM = re.compile(r'[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?')
ARGS = {'m': 2, 'l': 2, 'h': 1, 'v': 1, 'c': 6, 's': 4, 'q': 4, 't': 2, 'a': 7, 'z': 0}

def normalize(d):
    out, i, cmd, argi = [], 0, None, 0
    while i < len(d):
        ch = d[i]
        if ch in CMD:
            cmd, argi = ch, 0
            out.append(ch)
            i += 1
            continue
        if ch in ' ,\t\r\n':
            i += 1
            continue
        if cmd and cmd.lower() == 'a' and argi % 7 in (3, 4):
            if ch in '01':
                out.append(ch); argi += 1; i += 1
                continue
        m = NUM.match(d, i)
        if not m:
            raise ValueError(f'Unerwartetes Zeichen {ch!r} in {d[:60]}')
        tok = m.group(0)
        if tok.startswith('.'): tok = '0' + tok
        elif tok.startswith('-.'): tok = '-0' + tok[1:]
        elif tok.startswith('+'): tok = tok[1:]
        out.append(tok)
        argi += 1
        i = m.end()
    return ' '.join(out)

def paths(svg_text):
    ds = re.findall(r'<path[^>]*\sd="([^"]+)"', svg_text)
    if not ds or re.search(r'<(circle|rect|ellipse|polygon|polyline|line)\b', svg_text):
        return None
    return ' '.join(normalize(d) for d in ds)

def main(hero_dir, fluent_dir):
    result = {'hero': {}, 'fluent': {}}
    hd = os.path.join(hero_dir, '24', 'outline')
    for f in sorted(os.listdir(hd)):
        if f.endswith('.svg'):
            p = paths(open(os.path.join(hd, f), encoding='utf-8').read())
            if p: result['hero'][f[:-4]] = p
    fd = os.path.join(fluent_dir, 'icons')
    for f in sorted(os.listdir(fd)):
        if f.endswith('_24_regular.svg'):
            p = paths(open(os.path.join(fd, f), encoding='utf-8').read())
            if p: result['fluent'][f[:-len('_24_regular.svg')]] = p
    target = os.path.join(os.path.dirname(__file__), '..', 'src', 'PowerExec', 'Assets', 'icons.json')
    with open(target, 'w', encoding='utf-8') as fh:
        json.dump(result, fh, separators=(',', ':'), ensure_ascii=False)
    print(f"hero: {len(result['hero'])}, fluent: {len(result['fluent'])}, {os.path.getsize(target)//1024} KB")

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
