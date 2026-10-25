#!/usr/bin/env bash
# Декомпиляция всех функций игры (Assembly-CSharp) параллельно: N копий проекта Ghidra, у каждой своя часть списка.
# Результат: modkit/decomp/<Класс>/<Метод>_<RVA>.c (в git не добавлять). Уже готовые файлы пропускаются — можно перезапускать.
# Перед запуском остановите сервер Ghidra MCP (проект заблокирован). Запуск: bash modkit/tools/ghidra/decompile_all.sh [N] [таймаут_с]
set -e
N=${1:-6}; TIMEOUT=${2:-600}
G=/d/tools/ghidra; HERE=$(cd "$(dirname "$0")" && pwd); OUT=$(cd "$HERE/../.." && pwd)/decomp
W=$G/decompall; mkdir -p "$W" "$OUT"
export JAVA_HOME='D:\tools\jdk21\jdk-21.0.12.1+1'
# список функций игры из dump.cs (раздел Assembly-CSharp)
python "$HERE/gen_function_list.py" "$OUT/../dump.cs" "$W/functions.tsv"
for ((i=0; i<N; i++)); do
  P=$W/proj$i
  if [ ! -f "$P/zombie612.gpr" ]; then
    mkdir -p "$P"; cp "$G/project/zombie612.gpr" "$P/"; cp -r "$G/project/zombie612.rep" "$P/"
  fi
  rm -f "$P"/*.lock*
  "$G/ghidra_12.1.4_PUBLIC/support/analyzeHeadless.bat" "$(cygpath -w "$P")" zombie612 -process libil2cpp.so -noanalysis -readOnly \
    -scriptPath "$(cygpath -w "$HERE")" -postScript DecompileAll.java "$(cygpath -w "$W/functions.tsv")" "$(cygpath -w "$OUT")" $i $N $TIMEOUT \
    > "$W/part$i.log" 2>&1 &
done
wait
grep -h "ИТОГ" "$W"/part*.log
cat "$OUT"/failed_*.tsv 2>/dev/null | wc -l | xargs echo "не декомпилировано:"
