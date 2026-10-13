# C Studio : apprendre le C

Application Windows autonome : une seule fenêtre, sans onglets et sans navigateur. L’interface associe les blocs à glisser, le quadrillage relié, les explications au survol et l’éditeur C, dans un thème coloré inspiré des années 80.

## Démarrer depuis une clé USB

1. Copie le dossier `Apprendre-C` sur la clé.
2. Double-clique sur `CStudio.exe`.
3. Le dossier de la clé doit être accessible en écriture pour conserver les brouillons, quadrillages et explications personnelles.

## Créer et enregistrer un fichier C

- Clique sur **Nouveau** pour obtenir une page vierge. Tu peux aussi choisir un exemple dans la liste.
- Clique sur **Ouvrir** pour charger un fichier `.c`.
- Clique sur **Enregistrer**, ou utilise **Ctrl+S**, pour enregistrer le programme en `.c`. Choisis l’emplacement et le nom du fichier.
- Si tu changes de page avec des modifications non enregistrées, C Studio te propose de les enregistrer ou de les abandonner.
- Les brouillons des exemples et les quadrillages continuent d’être enregistrés à côté de `CStudio.exe` (`brouillon-*.c` et `quadrillage-*.txt`). Le glossaire personnel est dans `mon-glossaire.txt`.

## Compiler et exécuter

Clique sur **Exécuter** : C Studio enregistre le fichier `.c`, le compile avec **GCC** en C11, puis le lance avec **Windows PowerShell**. Les erreurs de compilation et la sortie du programme apparaissent dans la zone PowerShell. Le bouton **Arrêter** permet d’interrompre une exécution qui ne se termine pas.

Pour les programmes qui utilisent `scanf`, saisis les valeurs dans la case **Entrées clavier pour scanf** (une valeur par ligne), puis clique sur **Exécuter**.

L’exécution nécessite un compilateur GCC pour Windows. C Studio le recherche dans le `PATH`, dans quelques emplacements courants, ainsi qu’à ces emplacements facultatifs sur la clé :

- `mingw64\bin\gcc.exe`
- `gcc\bin\gcc.exe`

Si GCC n’est pas installé, C Studio l’indique clairement et conserve le fichier `.c` sans prétendre l’avoir exécuté. PowerShell est fourni avec Windows. N’exécute que du code auquel tu fais confiance.

## Utiliser l’atelier de blocs

- Maintiens le clic gauche sur un bloc de la palette, glisse-le dans le quadrillage, puis relâche-le pour le déposer.
- Déplace les blocs posés pour modifier leur ordre. Les fils indiquent l’enchaînement et le code C correspondant reste modifiable.
- Clique sur le `×` d’un bloc pour l’enlever.
- Survole un mot dans le code pour afficher son explication.
- **Synchroniser le code** remplace le texte modifié à la main par le code des blocs, après confirmation.
- **Mes explications** permet d’ajouter tes propres mots et définitions au glossaire.

## Recompiler C Studio

L’exécutable `CStudio.exe` est fourni. Pour le reconstruire, lance `build.bat` sur un PC avec le compilateur C# du .NET Framework.

