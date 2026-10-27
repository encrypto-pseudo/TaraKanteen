(() => {
  const products = [
    { id: "chicken-adobo-bowl", name: "Chicken Adobo Bowl", description: "Tender adobo, fluffy rice, happy lunch.", price: 89, category: "meals", icon: "🍛", art: "art-rice", badge: "Student favorite", rating: "★ 4.9" },
    { id: "cheesy-chicken-melt", name: "Cheesy Chicken Melt", description: "Toasty, cheesy, and made to go.", price: 65, category: "snacks", icon: "🥪", art: "art-sandwich", badge: "Fresh today", rating: "★ 4.8" },
    { id: "creamy-veggie-pasta", name: "Creamy Veggie Pasta", description: "A cozy bowl with a garden-fresh twist.", price: 75, category: "meals", icon: "🍝", art: "art-pasta", badge: "Veggie pick", rating: "★ 4.7" },
    { id: "mango-cooldown", name: "Mango Cooldown", description: "Real mango flavor for sunny afternoons.", price: 35, category: "drinks", icon: "🥭", art: "art-juice", badge: "Cool & fresh", rating: "★ 4.9" },
    { id: "savory-pancit", name: "Savory Pancit", description: "Classic noodles tossed with tasty toppings.", price: 60, category: "meals", icon: "🍜", art: "art-noodles", badge: "Campus classic", rating: "★ 4.8" },
    { id: "choco-chip-cookie", name: "Choco Chip Cookie", description: "Soft-baked and just the right amount sweet.", price: 25, category: "snacks", icon: "🍪", art: "art-cookie", badge: "Little treat", rating: "★ 4.9" },
    { id: "chilled-choco-milk", name: "Chilled Choco Milk", description: "A cold, creamy pick-me-up between classes.", price: 30, category: "drinks", icon: "🥛", art: "art-milk", badge: "Feel-good sip", rating: "★ 4.7" },
    { id: "fresh-fruit-cup", name: "Fresh Fruit Cup", description: "A colorful mix, cut fresh this morning.", price: 40, category: "snacks", icon: "🍓", art: "art-fruit", badge: "Made fresh", rating: "★ 4.8" }
  ];
  const productById = new Map(products.map(product => [product.id, product]));
  const storageKey = "taraKanteen.tray";

  function readItems() {
    const saved = localStorage.getItem(storageKey);
    if (!saved) return [];

    try {
      const entries = JSON.parse(saved);
      if (!Array.isArray(entries)) return [];
      return entries.filter(entry =>
        entry &&
        productById.has(entry.id) &&
        Number.isSafeInteger(entry.quantity) &&
        entry.quantity > 0
      );
    } catch (error) {
      console.error("Could not read the saved tray.", error);
      return [];
    }
  }

  function saveItems(items) {
    localStorage.setItem(storageKey, JSON.stringify(items));
    window.dispatchEvent(new Event("tara:cart-change"));
  }

  function getItems() {
    return readItems().map(entry => ({
      ...productById.get(entry.id),
      quantity: entry.quantity
    }));
  }

  function add(id) {
    if (!productById.has(id)) return;
    const items = readItems();
    const existing = items.find(item => item.id === id);
    if (existing) {
      existing.quantity += 1;
    } else {
      items.push({ id, quantity: 1 });
    }
    saveItems(items);
  }

  function setQuantity(id, quantity) {
    if (!productById.has(id) || !Number.isSafeInteger(quantity) || quantity < 1) return;
    const items = readItems();
    const existing = items.find(item => item.id === id);
    if (!existing) return;
    existing.quantity = quantity;
    saveItems(items);
  }

  function remove(id) {
    saveItems(readItems().filter(item => item.id !== id));
  }

  function updateBadge() {
    const count = getItems().reduce((total, item) => total + item.quantity, 0);
    document.querySelectorAll("[data-cart-count]").forEach(badge => {
      badge.textContent = String(count);
    });
    document.querySelectorAll("[data-cart-button]").forEach(button => {
      button.setAttribute("aria-label", `Your tray, ${count} ${count === 1 ? "item" : "items"}`);
    });
  }

  window.TaraKanteen = {
    products,
    cart: {
      add,
      getItems,
      getCount: () => getItems().reduce((total, item) => total + item.quantity, 0),
      getSubtotal: () => getItems().reduce((total, item) => total + item.price * item.quantity, 0),
      remove,
      setQuantity
    }
  };

  window.addEventListener("tara:cart-change", updateBadge);
  window.addEventListener("storage", event => {
    if (event.key === storageKey) updateBadge();
  });
  updateBadge();
})();
