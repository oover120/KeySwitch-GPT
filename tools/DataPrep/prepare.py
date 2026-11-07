"""Build-time only. Downloads attributed open data; no user data is used."""
import collections, concurrent.futures, gzip, hashlib, json, pathlib, re, urllib.request, struct
ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = ROOT / 'tools/DataPrep/cache'
CACHE.mkdir(parents=True, exist_ok=True)
(ROOT / 'assets').mkdir(exist_ok=True)
(ROOT / 'licenses').mkdir(exist_ok=True)
SOURCES = {}
for lang, repo, prefix in [('en','UD_English-EWT','en_ewt'),('ru','UD_Russian-GSD','ru_gsd')]:
    SOURCES[f'{lang}-full'] = f'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/{lang}/{lang}_full.txt'
    for split in ['train','test']:
        SOURCES[f'{lang}-{split}'] = f'https://raw.githubusercontent.com/UniversalDependencies/{repo}/r2.15/{prefix}-ud-{split}.conllu'
    for kind, filename in [('license','LICENSE.txt'),('readme','README.md')]:
        SOURCES[f'{lang}-{kind}'] = f'https://raw.githubusercontent.com/UniversalDependencies/{repo}/r2.15/{filename}'
SOURCES['frequency-readme'] = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/README.md'
SOURCES['cc-by-sa-4'] = SOURCES['en-license']  # Upstream ships the full CC legal text.
def fetch(item):
    name, url = item
    path = CACHE / (name+'.txt')
    if not path.exists():
        with urllib.request.urlopen(url, timeout=90) as response: path.write_bytes(response.read())
    raw = path.read_bytes()
    return name, raw, {'url':url,'sha256':hashlib.sha256(raw).hexdigest()}
downloaded = list(concurrent.futures.ThreadPoolExecutor(max_workers=6).map(fetch,SOURCES.items()))
data = {name:raw.decode('utf-8-sig') for name,raw,_ in downloaded}
manifest = {name:info for name,_,info in downloaded}
def words(conllu, lang):
    pattern = re.compile('[a-z]+' if lang == 'en' else '[а-яё]+')
    for line in conllu.splitlines():
        columns = line.split('\t')
        if len(columns)>2 and columns[0].isdigit():
            word = columns[1].lower()
            if pattern.fullmatch(word): yield word
for lang in ['en','ru']:
    pattern = re.compile('[a-z]+' if lang=='en' else '[а-яё]+')
    frequency = {}
    for line in data[lang+'-full'].splitlines():
        word,count = line.rsplit(' ',1)
        if pattern.fullmatch(word) and len(word)<=48: frequency[word]=int(count)
        if len(frequency)>=150000: break
    grams = collections.Counter()
    for word in words(data[lang+'-train'],lang):
        padded = '^'+word+'$'
        for n in (3,4):
            for i in range(len(padded)-n+1): grams[padded[i:i+n]]+=1
    def packed(mapping):
        items=sorted((word.encode('utf-8'),count) for word,count in mapping.items())
        pool=b''.join(word for word,_ in items)
        offsets=[0]
        for word,_ in items: offsets.append(offsets[-1]+len(word))
        return struct.pack('<ii',len(items),len(pool))+pool+struct.pack('<'+'i'*len(offsets),*offsets)+struct.pack('<'+'i'*len(items),*(count for _,count in items))
    payload=b'KSM1'+packed(frequency)+packed(grams)
    (ROOT/'assets'/f'{lang}.ksm.gz').write_bytes(gzip.compress(payload,mtime=0))
    holdout = collections.Counter(words(data[lang+'-test'],lang))
    (ROOT/'assets'/f'{lang}-holdout.tsv').write_text('\n'.join(f'{w}\t{c}' for w,c in sorted(holdout.items()))+'\n',encoding='utf-8')
    (ROOT/'licenses'/f'UD-{lang}-LICENSE.txt').write_text(data[lang+'-license'],encoding='utf-8')
    (ROOT/'licenses'/f'UD-{lang}-README.md').write_text(data[lang+'-readme'],encoding='utf-8')
    print(lang, 'dictionary',len(frequency),'grams',len(grams),'holdout',len(holdout))
for name in ['frequency-readme','cc-by-sa-4']:
    (ROOT/'licenses'/f'{name}.txt').write_text(data[name],encoding='utf-8')
(ROOT/'assets'/'sources.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
