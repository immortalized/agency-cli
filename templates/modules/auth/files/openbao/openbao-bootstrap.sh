#!/bin/sh

set -eu
umask 077

material_directory=/openbao/dev-bootstrap
material_file="$material_directory/openbao-init.txt"
pending_material_file="$material_directory/openbao-init.pending"

mkdir -p "$material_directory"

wait_for_openbao() {
  while true; do
    if bao status >/dev/null 2>&1; then
      return
    else
      status=$?
    fi

    if [ "$status" -eq 2 ]; then
      return
    fi

    sleep 1
  done
}

initialize_if_needed() {
  if bao operator init -status >/dev/null 2>&1; then
    return
  else
    status=$?
  fi

  if [ "$status" -ne 2 ]; then
    echo "Unable to determine whether development OpenBao is initialized." >&2
    return 1
  fi

  echo "Initializing fresh development OpenBao storage..."
  bao operator init \
    -key-shares=1 \
    -key-threshold=1 \
    > "$pending_material_file"

  chmod 600 "$pending_material_file"
  mv "$pending_material_file" "$material_file"
  echo "Development OpenBao initialization material stored in its private Docker volume."
}

load_unseal_key() {
  if [ ! -f "$material_file" ] && [ -f "$pending_material_file" ]; then
    mv "$pending_material_file" "$material_file"
  fi

  if [ ! -f "$material_file" ]; then
    echo "Development OpenBao is initialized, but its unseal material is missing." >&2
    echo "For a disposable development stack, run 'docker compose down -v' and start again." >&2
    return 1
  fi

  unseal_key="$(sed -n 's/^Unseal Key 1: //p' "$material_file")"
  if [ -z "$unseal_key" ]; then
    echo "The development OpenBao unseal material is invalid." >&2
    return 1
  fi
}

unseal_if_needed() {
  if bao status >/dev/null 2>&1; then
    return
  else
    status=$?
  fi

  if [ "$status" -ne 2 ]; then
    return
  fi

  load_unseal_key
  bao operator unseal "$unseal_key" >/dev/null
  echo "Development OpenBao is unsealed."
}

while true; do
  wait_for_openbao
  initialize_if_needed
  unseal_if_needed
  sleep 5
done
