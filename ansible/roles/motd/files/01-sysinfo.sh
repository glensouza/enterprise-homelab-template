#!/bin/bash

# Define Colors
RED='\033[0;31m'
GRN='\033[0;32m'
YLW='\033[0;33m'
BLU='\033[0;34m'
NC='\033[0m'

# Gather Data
DISK_USAGE=$(df / | awk 'NR==2 {print $5}' | sed 's/%//')
MEM_USAGE=$(free -m | awk '/Mem:/ { printf("%3.1f", $3/$2*100) }')
# LXCs don't expose their own thermal zone - this stays N/A on every current
# host in the fleet, kept for parity with bare-metal/VM hosts this same
# script also runs on.
TEMP_RAW=$(cat /sys/class/thermal/thermal_zone0/temp 2>/dev/null)
if [ -n "$TEMP_RAW" ]; then
    TEMP=$(awk -v t="$TEMP_RAW" 'BEGIN { printf "%.1f°C", t / 1000 }')
else
    TEMP="N/A"
fi

# Color Logic
[ "$DISK_USAGE" -gt 85 ] && DISK_COL=$RED || DISK_COL=$GRN
[[ ${MEM_USAGE%.*} -gt 85 ]] && MEM_COL=$RED || MEM_COL=$GRN

# Draw the Box
echo -e "${BLU}┌────────────────────────────────────────┐${NC}"
echo -e "${BLU}│${NC}  ${YLW}SYSTEM STATUS REPORT${NC}                  ${BLU}│${NC}"
echo -e "${BLU}├────────────────────────────────────────┤${NC}"
printf "${BLU}│${NC}  Root Disk Usage:  ${DISK_COL}%-18s${NC}  ${BLU}│${NC}\n" "${DISK_USAGE}%"
printf "${BLU}│${NC}  Memory Usage:     ${MEM_COL}%-18s${NC}  ${BLU}│${NC}\n" "${MEM_USAGE}%"
printf "${BLU}│${NC}  CPU Temperature:  ${GRN}%-18s${NC}   ${BLU}│${NC}\n" "$TEMP"
echo -e "${BLU}└────────────────────────────────────────┘${NC}"
