# DEVELOPMENT ONLY. This is a single-node, HTTP OpenBao server.
# Production deployments need their own TLS, seal, storage, and HA design.
ui = false
disable_mlock = true

storage "raft" {
  path    = "/openbao/file"
  node_id = "__PROJECT_SLUG__-development"
}

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = true
}

api_addr     = "http://openbao:8200"
cluster_addr = "http://openbao:8201"

# Keep the restricted development API token usable across ordinary restarts.
# This does not replace production token lifecycle management.
default_lease_ttl = "8760h"
max_lease_ttl     = "8760h"
