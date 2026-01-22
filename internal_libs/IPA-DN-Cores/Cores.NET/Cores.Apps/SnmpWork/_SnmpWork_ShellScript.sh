#!/bin/bash

# 2020/05/05
# tune glibc memory allocation, optimize for low fragmentation
# limit the number of arenas
export MALLOC_ARENA_MAX=2
# disable dynamic mmap threshold, see M_MMAP_THRESHOLD in "man mallopt"
export MALLOC_MMAP_THRESHOLD_=131072
export MALLOC_TRIM_THRESHOLD_=131072
export MALLOC_TOP_PAD_=131072
export MALLOC_MMAP_MAX_=65536
export HOME=/root

mkdir -p /etc/se_snmpwork/Local/App_TestDev/Config/
ln -s /etc/se_snmpwork/Local/App_TestDev/ /etc/se_snmpwork/Local/App_SnmpWork


echo test > /etc/se_snmpwork/approot

/etc/se_snmpwork/tmp/snmpwork/se_snmpwork2 $@


