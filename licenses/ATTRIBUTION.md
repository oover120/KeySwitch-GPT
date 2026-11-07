# Language data attribution

KeySwitch uses independently implemented code. Language data is redistributed and transformed separately under Creative Commons Attribution-ShareAlike 4.0 International; see `cc-by-sa-4.txt`.

- **FrequencyWords 2018** by **Hermit Dave**, based on **OpenSubtitles 2018 / OPUS**. Source: https://github.com/hermitdave/FrequencyWords. Upstream explicitly licenses content under CC BY-SA 4.0 (its code is MIT and is not copied here). Changes: keep alphabetic forms only, retain up to 150,000 entries per language, pack words and counts in sorted UTF-8 pools and compress them. See `frequency-readme.txt`. OpenSubtitles corpus information: https://opus.nlpl.eu/OpenSubtitles.php.
- **Universal Dependencies English EWT r2.15**: annotations © 2013–2021 The Board of Trustees of The Leland Stanford Junior University; contributors credited in `UD-en-README.md`. Source: https://github.com/UniversalDependencies/UD_English-EWT/tree/r2.15. Annotation/database license CC BY-SA 4.0. Underlying texts have separate rights as described upstream. Changes: aggregate alphabetic train tokens into boundary-aware character trigram/fourgram counts; test tokens reduced to isolated word/count lists. No original sentences are shipped. License: `UD-en-LICENSE.txt`.
- **Universal Dependencies Russian GSD r2.15**, Google treebank; Ryan McDonald, Vitaly Nikolaev, Olga Lyashevskaya and other contributors credited upstream. Source: https://github.com/UniversalDependencies/UD_Russian-GSD/tree/r2.15. Annotation/database license CC BY-SA 4.0; upstream disclaims ownership of underlying Wikipedia content. Changes: same aggregate n-gram and word-count extraction as above. See `UD-ru-README.md`, `UD-ru-LICENSE.txt`.

The derived `assets/*.ksm.gz` models and `assets/*-holdout.tsv` data remain CC BY-SA 4.0. Reproduction code: `tools/DataPrep/prepare.py`; downloaded input URLs and SHA-256: `assets/sources.json`. No endorsement by the original authors is implied.

The Creative Commons legal text was retrieved from UD English EWT's LICENSE.txt (the same CC BY-SA 4.0 legal code) after the direct Creative Commons download returned HTTP 403.
