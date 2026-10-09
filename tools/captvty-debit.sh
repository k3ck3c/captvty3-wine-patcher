#!/bin/bash

declare -A precedent

while true; do
    declare -A courant=()
    total=0

    while read -r inode octets; do
        courant[$inode]=$octets

        if [[ -v precedent[$inode] ]]; then
            delta=$((octets - precedent[$inode]))
        else
            delta=0
        fi

        (( delta > 0 )) && (( total += delta ))
    done < <(
        ss -tnpie 2>/dev/null | awk '
        /^ESTAB/ { captvty=0; inode="" }
        /Captvty.exe/ {
            captvty=1
            if (match($0,/ino:[0-9]+/))
                inode=substr($0,RSTART+4,RLENGTH-4)
        }
        /bytes_received:/ && captvty && inode!="" {
            if (match($0,/bytes_received:[0-9]+/)) {
                n=substr($0,RSTART+15,RLENGTH-15)
                print inode,n
            }
        }'
    )

    printf '%(%H:%M:%S)T  Débit reçu : %8.2f Mio/s  Connexions : %d\n' \
        -1 \
        "$(awk -v n="$total" 'BEGIN {printf "%.2f", n/1048576}')" \
        "${#courant[@]}"

    precedent=()
    for inode in "${!courant[@]}"; do
        precedent[$inode]=${courant[$inode]}
    done

    sleep 1
done
