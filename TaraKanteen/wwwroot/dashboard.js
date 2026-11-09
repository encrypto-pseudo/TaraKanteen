(() => {
  const dashboard = document.body.dataset.dashboard;
  const notice = document.querySelector("#dashboardNotice");
  const money = amount => `₱${Number(amount).toFixed(2)}`;
  const escapeHtml = value => String(value).replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);

  async function api(url, options = {}) {
    const response = await fetch(url, {
      ...options,
      headers: { ...(options.body ? { "Content-Type": "application/json" } : {}), ...options.headers }
    });
    const result = response.status === 204 || !response.headers.get("content-type")?.includes("application/json")
      ? null
      : await response.json();
    if (!response.ok) {
      if (response.status === 401) {
        window.location.href = "login.html";
        throw new Error("Please log in to continue.");
      }
      throw new Error(result?.message || "The request could not be completed.");
    }
    return result;
  }

  function showNotice(message, success = false) {
    notice.textContent = message;
    notice.classList.toggle("success", success);
  }

  function formatDate(value) {
    return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
  }

  async function start() {
    try {
      const user = await api("/api/me");
      const expectedRole = dashboard === "admin" ? "Admin" : "User";
      if (user.role !== expectedRole) {
        window.location.href = user.role === "Admin" ? "admin.html" : "student.html";
        return;
      }
      document.querySelector("[data-user-name]").textContent = user.username;
      document.querySelector("[data-logout]").addEventListener("click", async () => {
        try {
          await api("/api/logout", { method: "POST" });
          window.location.href = "index.html";
        } catch (error) {
          showNotice(error.message);
        }
      });
      if (dashboard === "admin") await startAdmin();
      else await startStudent();
    } catch (error) {
      showNotice(error.message);
    }
  }

  async function startAdmin() {
    document.querySelector("#dashboardDate").textContent =
      new Date().toLocaleDateString([], { weekday: "long", month: "long", day: "numeric" });
    const form = document.querySelector("#productForm");
    const productsTable = document.querySelector("#productsTable");
    const ordersTable = document.querySelector("#ordersTable");
    const counterOrderForm = document.querySelector("#counterOrderForm");
    const editCancel = document.querySelector("#cancelEditButton");
    let dashboardData;

    function renderProducts(products) {
      productsTable.innerHTML = products.length ? products.map(product => `
        <tr>
          <td><span class="table-product-icon">${escapeHtml(product.icon)}</span><strong>${escapeHtml(product.name)}</strong></td>
          <td class="capitalize">${escapeHtml(product.category)}</td>
          <td>${money(product.price)}</td>
          <td><span class="${product.stock <= 5 ? "stock-low" : ""}">${product.stock}</span></td>
          <td><span class="status-pill ${product.isAvailable ? "status-available" : "status-unavailable"}">${product.isAvailable ? "Available" : "Hidden"}</span></td>
          <td><button class="table-action" type="button" data-edit-product="${product.id}">Edit</button></td>
        </tr>`).join("") : '<tr><td colspan="6" class="table-empty">No products yet. Add your first menu item above.</td></tr>';
    }

    function renderOrders(orders) {
      ordersTable.innerHTML = orders.length ? orders.map(order => `
        <tr>
          <td><strong>#${order.id}</strong></td>
          <td>${escapeHtml(order.studentUsername)}</td>
          <td>${order.items.map(item => `${item.quantity} × ${escapeHtml(item.productName)}`).join("<br>")}</td>
          <td><strong>${money(order.total)}</strong></td>
          <td>${formatDate(order.createdAt)}</td>
          <td><select class="status-select" data-order-status="${order.id}" aria-label="Update status for order ${order.id}">
            ${["Preparing", "Ready", "Completed", "Cancelled"].map(status => `<option${status === order.status ? " selected" : ""}>${status}</option>`).join("")}
          </select></td>
        </tr>`).join("") : '<tr><td colspan="6" class="table-empty">No transactions recorded yet.</td></tr>';
    }

    function render(data) {
      dashboardData = data;
      document.querySelector("#metricSales").textContent = money(data.metrics.todaySales);
      document.querySelector("#metricOrders").textContent = data.metrics.todayOrders;
      document.querySelector("#metricProducts").textContent = data.metrics.activeProducts;
      document.querySelector("#metricLowStock").textContent = data.metrics.lowStock;
      const selectedProduct = document.querySelector("#counterProduct").value;
      document.querySelector("#counterProduct").innerHTML = '<option value="">Select a product</option>' +
        data.products.filter(product => product.isAvailable && product.stock > 0).map(product =>
          `<option value="${product.id}">${escapeHtml(product.name)} · ${money(product.price)} · ${product.stock} left</option>`
        ).join("");
      if (data.products.some(product => String(product.id) === selectedProduct && product.isAvailable && product.stock > 0))
        document.querySelector("#counterProduct").value = selectedProduct;
      renderProducts(data.products);
      renderOrders(data.orders);
    }

    async function refresh() {
      render(await api("/api/admin/dashboard"));
    }

    function resetProductForm() {
      form.reset();
      document.querySelector("#productId").value = "";
      document.querySelector("#productIcon").value = "🍽️";
      document.querySelector("#productAvailable").checked = true;
      document.querySelector("#saveProductButton").textContent = "Add product";
      editCancel.hidden = true;
    }

    form.addEventListener("submit", async event => {
      event.preventDefault();
      const id = document.querySelector("#productId").value;
      const product = {
        name: document.querySelector("#productName").value,
        description: document.querySelector("#productDescription").value,
        price: Number(document.querySelector("#productPrice").value),
        category: document.querySelector("#productCategory").value,
        icon: document.querySelector("#productIcon").value,
        stock: Number(document.querySelector("#productStock").value),
        isAvailable: document.querySelector("#productAvailable").checked
      };
      try {
        await api(id ? `/api/admin/products/${id}` : "/api/admin/products", {
          method: id ? "PUT" : "POST", body: JSON.stringify(product)
        });
        resetProductForm();
        await refresh();
        showNotice(id ? "Product and inventory updated." : "Product added to the menu.", true);
      } catch (error) {
        showNotice(error.message);
      }
    });

    productsTable.addEventListener("click", event => {
      const button = event.target.closest("[data-edit-product]");
      if (!button) return;
      const product = dashboardData.products.find(item => item.id === Number(button.dataset.editProduct));
      if (!product) return;
      document.querySelector("#productId").value = product.id;
      document.querySelector("#productName").value = product.name;
      document.querySelector("#productDescription").value = product.description;
      document.querySelector("#productPrice").value = product.price;
      document.querySelector("#productCategory").value = product.category;
      document.querySelector("#productIcon").value = product.icon;
      document.querySelector("#productStock").value = product.stock;
      document.querySelector("#productAvailable").checked = product.isAvailable;
      document.querySelector("#saveProductButton").textContent = "Save changes";
      editCancel.hidden = false;
      form.scrollIntoView({ behavior: "smooth", block: "center" });
    });
    editCancel.addEventListener("click", resetProductForm);
    counterOrderForm.addEventListener("submit", async event => {
      event.preventDefault();
      const submit = counterOrderForm.querySelector('button[type="submit"]');
      submit.disabled = true;
      try {
        await api("/api/admin/orders", {
          method: "POST",
          body: JSON.stringify({
            productId: Number(document.querySelector("#counterProduct").value),
            quantity: Number(document.querySelector("#counterQuantity").value),
            customerName: document.querySelector("#counterCustomer").value
          })
        });
        counterOrderForm.reset();
        await refresh();
        showNotice("Counter sale recorded and inventory updated.", true);
      } catch (error) {
        showNotice(error.message);
        await refresh();
      } finally {
        submit.disabled = false;
      }
    });
    ordersTable.addEventListener("change", async event => {
      const select = event.target.closest("[data-order-status]");
      if (!select) return;
      try {
        await api(`/api/admin/orders/${select.dataset.orderStatus}/status`, {
          method: "PUT", body: JSON.stringify({ status: select.value })
        });
        await refresh();
        showNotice("Order status updated.", true);
      } catch (error) {
        showNotice(error.message);
        await refresh();
      }
    });
        // Load orders immediately.
    await refresh();

    // Automatically refresh the admin dashboard every 5 seconds.
    window.setInterval(async () => {
      if (document.visibilityState !== "visible") return;

      try {
        await refresh();
      } catch (error) {
        showNotice(error.message);
      }
    }, 5000);
  }

  async function startStudent() {
    const productList = document.querySelector("#studentProducts");
    const cartList = document.querySelector("#studentCartItems");
    const search = document.querySelector("#studentSearch");
    const cart = new Map();
    let products = [];
    let activeCategory = "all";

    function renderProducts() {
      const query = search.value.trim().toLowerCase();
      const matching = products.filter(product =>
        (activeCategory === "all" || product.category === activeCategory) &&
        `${product.name} ${product.description}`.toLowerCase().includes(query)
      );
      productList.innerHTML = matching.length ? matching.map(product => `
        <article class="student-product-card">
          <div class="student-product-icon" aria-hidden="true">${escapeHtml(product.icon)}</div>
          <div class="student-product-info">
            <span class="product-category">${escapeHtml(product.category)}</span>
            <h3>${escapeHtml(product.name)}</h3><p>${escapeHtml(product.description)}</p>
            <div class="student-product-bottom"><strong>${money(product.price)}</strong>
              <button class="dashboard-secondary add-order-button" type="button" data-add-product="${product.id}" ${cart.get(product.id) >= product.stock ? "disabled" : ""}>Add</button>
            </div>
            <span class="stock-note">${product.stock} available</span>
          </div>
        </article>`).join("") : '<p class="table-empty">No available items match your search.</p>';
    }

    function renderCart() {
      const items = [...cart.entries()].map(([id, quantity]) => ({
        product: products.find(product => product.id === id), quantity
      })).filter(item => item.product);
      const count = items.reduce((total, item) => total + item.quantity, 0);
      const total = items.reduce((sum, item) => sum + item.product.price * item.quantity, 0);
      document.querySelector("#studentCartCount").textContent = count;
      document.querySelector("#studentCartTotal").textContent = money(total);
      document.querySelector("#placeOrderButton").disabled = items.length === 0;
      cartList.innerHTML = items.length ? items.map(({ product, quantity }) => `
        <div class="student-cart-row"><div><strong>${escapeHtml(product.name)}</strong><span>${money(product.price)} each</span></div>
          <div class="cart-quantity"><button type="button" data-cart-change="${product.id}" data-change="-1" aria-label="Remove one ${escapeHtml(product.name)}">−</button><span>${quantity}</span><button type="button" data-cart-change="${product.id}" data-change="1" aria-label="Add one ${escapeHtml(product.name)}">+</button></div>
        </div>`).join("") : '<p class="order-empty">Add something tasty from today’s menu.</p>';
    }

    async function refreshProducts() {
      products = await api("/api/products");
      for (const id of cart.keys()) {
        const product = products.find(item => item.id === id);
        if (!product) cart.delete(id);
        else if (cart.get(id) > product.stock) cart.set(id, product.stock);
      }
      renderProducts();
      renderCart();
    }

    async function refreshOrders() {
      const orders = await api("/api/orders/mine");
      document.querySelector("#studentOrderHistory").innerHTML = orders.length ? orders.slice(0, 5).map(order => `
        <article class="history-order">
          <div class="history-order-top"><strong>Order #${order.id}</strong><span class="status-pill status-${order.status.toLowerCase()}">${escapeHtml(order.status)}</span></div>
          <p>${order.items.map(item => `${item.quantity} × ${escapeHtml(item.productName)}`).join(", ")}</p>
          <div class="history-order-bottom"><span>${formatDate(order.createdAt)}</span><strong>${money(order.total)}</strong></div>
        </article>`).join("") : '<p class="order-empty">Your placed orders will appear here.</p>';
    }

    document.querySelectorAll(".student-menu .category").forEach(button => {
      button.addEventListener("click", () => {
        activeCategory = button.dataset.category;
        document.querySelectorAll(".student-menu .category").forEach(category => {
          const selected = category === button;
          category.classList.toggle("active", selected);
          category.setAttribute("aria-pressed", String(selected));
        });
        renderProducts();
      });
    });
    search.addEventListener("input", renderProducts);
    productList.addEventListener("click", event => {
      const button = event.target.closest("[data-add-product]");
      if (!button) return;
      const id = Number(button.dataset.addProduct);
      cart.set(id, (cart.get(id) || 0) + 1);
      renderProducts();
      renderCart();
    });
    cartList.addEventListener("click", event => {
      const button = event.target.closest("[data-cart-change]");
      if (!button) return;
      const id = Number(button.dataset.cartChange);
      const next = (cart.get(id) || 0) + Number(button.dataset.change);
      if (next <= 0) cart.delete(id);
      else cart.set(id, next);
      renderProducts();
      renderCart();
    });
    document.querySelector("#placeOrderButton").addEventListener("click", async event => {
      const button = event.currentTarget;
      button.disabled = true;
      try {
        await api("/api/orders", {
          method: "POST",
          body: JSON.stringify({ items: [...cart.entries()].map(([productId, quantity]) => ({ productId, quantity })) })
        });
        cart.clear();
        await Promise.all([refreshProducts(), refreshOrders()]);
        showNotice("Order placed! The canteen is getting it ready.", true);
      } catch (error) {
        showNotice(error.message);
        await refreshProducts();
      } finally {
        button.disabled = cart.size === 0;
      }
    });
    await Promise.all([refreshProducts(), refreshOrders()]);
  }

  start();
})();
